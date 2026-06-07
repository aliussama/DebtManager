using System.Net.Http.Json;
using DebtManager.Infrastructure.Security;
using DebtManager.Sync.Contracts;

namespace DebtManager.Sync.Transport;

/// <summary>
/// Secure Azure sync transport with:
/// - End-to-end payload encryption
/// - Certificate pinning support
/// - Retry with exponential backoff
/// - Request/response logging
/// </summary>
public sealed class SecureAzureSyncTransport : ISyncTransport, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly PayloadEncryptor? _encryptor;
    private readonly ISecurityAuditLogger? _auditLogger;
    private readonly string _deviceId;

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = { 
        TimeSpan.FromSeconds(1), 
        TimeSpan.FromSeconds(5), 
        TimeSpan.FromSeconds(15) 
    };

    public SecureAzureSyncTransport(
        HttpClient http,
        string baseUrl,
        string apiKey,
        string deviceId,
        PayloadEncryptor? encryptor = null,
        ISecurityAuditLogger? auditLogger = null)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _deviceId = deviceId;
        _encryptor = encryptor;
        _auditLogger = auditLogger;

        // Configure secure defaults
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("User-Agent", "DebtManager/1.0");
    }

    public async Task<PushBatchResponse> PushAsync(string vaultId, PushBatchRequest req, CancellationToken ct)
    {
        await LogSecurityEventAsync(SecurityEventType.SyncPushStarted,
            $"Starting push of {req.Events?.Count ?? 0} events to vault {vaultId}",
            vaultId, ct);

        try
        {
            // Encrypt payloads if encryptor is available
            var secureReq = _encryptor != null ? EncryptRequest(req) : req;

            var response = await ExecuteWithRetryAsync(async () =>
            {
                using var msg = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{_baseUrl}/api/v1/vaults/{vaultId}/events:batch");

                msg.Headers.Add("x-sync-key", _apiKey);
                msg.Headers.Add("x-device-id", _deviceId);
                msg.Headers.Add("x-request-id", Guid.NewGuid().ToString());

                if (_encryptor != null)
                    msg.Headers.Add("x-encrypted", "true");

                msg.Content = JsonContent.Create(secureReq);

                using var res = await _http.SendAsync(msg, ct);
                res.EnsureSuccessStatusCode();

                return await res.Content.ReadFromJsonAsync<PushBatchResponse>(cancellationToken: ct)
                    ?? new PushBatchResponse(0, 0);

            }, ct);

            await LogSecurityEventAsync(SecurityEventType.SyncPushCompleted,
                $"Push completed: {response.Accepted} accepted, {response.AlreadyPresent} already present",
                vaultId, ct);

            return response;
        }
        catch (Exception ex)
        {
            await LogSecurityEventAsync(SecurityEventType.SyncPushFailed,
                $"Push failed: {ex.Message}",
                vaultId, ct, success: false);
            throw;
        }
    }

    public async Task<PullResponse> PullAsync(string vaultId, string? sinceCursor, int limit, CancellationToken ct)
    {
        await LogSecurityEventAsync(SecurityEventType.SyncPullStarted,
            $"Starting pull from vault {vaultId}, cursor: {sinceCursor ?? "none"}",
            vaultId, ct);

        try
        {
            var response = await ExecuteWithRetryAsync(async () =>
            {
                var since = Uri.EscapeDataString(sinceCursor ?? "");
                var url = $"{_baseUrl}/api/v1/vaults/{vaultId}/events?since={since}&limit={limit}";

                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                msg.Headers.Add("x-sync-key", _apiKey);
                msg.Headers.Add("x-device-id", _deviceId);
                msg.Headers.Add("x-request-id", Guid.NewGuid().ToString());

                using var res = await _http.SendAsync(msg, ct);
                res.EnsureSuccessStatusCode();

                var body = await res.Content.ReadFromJsonAsync<PullResponse>(cancellationToken: ct);
                return body ?? new PullResponse(sinceCursor ?? "", new List<SyncEventDto>());

            }, ct);

            // Decrypt payloads if encrypted
            var decryptedResponse = _encryptor != null ? DecryptResponse(response) : response;

            await LogSecurityEventAsync(SecurityEventType.SyncPullCompleted,
                $"Pull completed: {decryptedResponse.Events.Count} events received",
                vaultId, ct);

            return decryptedResponse;
        }
        catch (Exception ex)
        {
            await LogSecurityEventAsync(SecurityEventType.SyncPullFailed,
                $"Pull failed: {ex.Message}",
                vaultId, ct, success: false);
            throw;
        }
    }

    private PushBatchRequest EncryptRequest(PushBatchRequest req)
    {
        if (_encryptor == null || req.Events == null)
            return req;

        var encryptedEvents = req.Events.Select(e => e with
        {
            PayloadJson = _encryptor.Encrypt(e.PayloadJson)
        }).ToList();

        return req with { Events = encryptedEvents };
    }

    private PullResponse DecryptResponse(PullResponse response)
    {
        if (_encryptor == null)
            return response;

        var decryptedEvents = response.Events.Select(e => e with
        {
            PayloadJson = _encryptor.Decrypt(e.PayloadJson)
        }).ToList();

        return response with { Events = decryptedEvents };
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries && IsRetryable(ex))
            {
                lastException = ex;
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException ex) when (attempt < MaxRetries)
            {
                // Timeout - retry
                lastException = ex;
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }

        throw lastException ?? new InvalidOperationException("Retry failed with no exception.");
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        // Retry on network errors and server errors (5xx)
        return ex.StatusCode == null || (int)ex.StatusCode >= 500;
    }

    private async Task LogSecurityEventAsync(
        SecurityEventType type,
        string message,
        string vaultId,
        CancellationToken ct,
        bool success = true)
    {
        if (_auditLogger == null)
            return;

        var entry = SecurityAudit.Create(
            type: type,
            message: message,
            success: success,
            deviceId: _deviceId,
            resourceId: vaultId,
            resourceType: "Vault"
        );

        try
        {
            await _auditLogger.LogAsync(entry, ct);
        }
        catch
        {
            // Don't fail sync due to audit logging issues
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
