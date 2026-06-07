using System.Net.Http.Json;
using DebtManager.Sync.Contracts;

namespace DebtManager.Sync.Transport;

public sealed class AzureSyncTransport : ISyncTransport
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public AzureSyncTransport(HttpClient http, string baseUrl, string apiKey)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
    }

    public async Task<PushBatchResponse> PushAsync(string vaultId, PushBatchRequest req, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/api/v1/vaults/{vaultId}/events:batch");

        msg.Headers.Add("x-sync-key", _apiKey);
        msg.Content = JsonContent.Create(req);

        using var res = await _http.SendAsync(msg, ct);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<PushBatchResponse>(cancellationToken: ct);
        return body ?? new PushBatchResponse(0, 0);
    }

    public async Task<PullResponse> PullAsync(string vaultId, string? sinceCursor, int limit, CancellationToken ct)
    {
        var since = Uri.EscapeDataString(sinceCursor ?? "");
        var url = $"{_baseUrl}/api/v1/vaults/{vaultId}/events?since={since}&limit={limit}";

        using var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Add("x-sync-key", _apiKey);

        using var res = await _http.SendAsync(msg, ct);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<PullResponse>(cancellationToken: ct);
        return body ?? new PullResponse(sinceCursor ?? "", new List<SyncEventDto>());
    }
}
