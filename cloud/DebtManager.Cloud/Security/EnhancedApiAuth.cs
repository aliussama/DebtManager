using Microsoft.Azure.Functions.Worker.Http;
using System.Collections.Concurrent;
using System.Net;

namespace DebtManager.Cloud.Security;

/// <summary>
/// Enhanced API authentication with:
/// - API key validation
/// - Rate limiting
/// - Device tracking
/// - Request validation
/// </summary>
public static class EnhancedApiAuth
{
    private static readonly ConcurrentDictionary<string, RateLimitBucket> _rateLimits = new();

    // Rate limit configuration
    private const int MaxRequestsPerMinute = 60;
    private const int MaxRequestsPerHour = 1000;
    private const int MaxPayloadSizeBytes = 5_000_000; // 5MB

    /// <summary>
    /// Validate request with full security checks.
    /// </summary>
    public static async Task<AuthResult> ValidateRequestAsync(
        HttpRequestData req,
        long? contentLength = null)
    {
        // 1. Check API key
        var apiKeyResult = ValidateApiKey(req);
        if (!apiKeyResult.IsValid)
            return apiKeyResult;

        // 2. Check rate limits
        var rateLimitResult = CheckRateLimit(req, apiKeyResult.DeviceId ?? "unknown");
        if (!rateLimitResult.IsValid)
            return rateLimitResult;

        // 3. Check payload size
        if (contentLength.HasValue && contentLength.Value > MaxPayloadSizeBytes)
            return AuthResult.Fail("Payload too large", HttpStatusCode.RequestEntityTooLarge);

        // 4. Validate required headers
        if (!req.Headers.TryGetValues("x-device-id", out _))
        {
            // Not required, but log for monitoring
        }

        return AuthResult.Success(apiKeyResult.DeviceId);
    }

    private static AuthResult ValidateApiKey(HttpRequestData req)
    {
        var expected = Environment.GetEnvironmentVariable("SyncApiKey");

        // Dev mode: if no key configured, allow all
        if (string.IsNullOrWhiteSpace(expected))
            return AuthResult.Success(GetDeviceId(req));

        if (!req.Headers.TryGetValues("x-sync-key", out var values))
            return AuthResult.Fail("Missing API key", HttpStatusCode.Unauthorized);

        var actual = values.FirstOrDefault();

        // Constant-time comparison to prevent timing attacks
        if (!SecureCompare(expected, actual))
            return AuthResult.Fail("Invalid API key", HttpStatusCode.Unauthorized);

        return AuthResult.Success(GetDeviceId(req));
    }

    private static AuthResult CheckRateLimit(HttpRequestData req, string deviceId)
    {
        var now = DateTimeOffset.UtcNow;
        var bucket = _rateLimits.GetOrAdd(deviceId, _ => new RateLimitBucket());

        lock (bucket)
        {
            // Clean old entries
            bucket.CleanOldEntries(now);

            // Check minute limit
            var minuteCount = bucket.GetMinuteCount(now);
            if (minuteCount >= MaxRequestsPerMinute)
                return AuthResult.Fail(
                    $"Rate limit exceeded ({MaxRequestsPerMinute}/minute)",
                    HttpStatusCode.TooManyRequests);

            // Check hour limit
            var hourCount = bucket.GetHourCount(now);
            if (hourCount >= MaxRequestsPerHour)
                return AuthResult.Fail(
                    $"Rate limit exceeded ({MaxRequestsPerHour}/hour)",
                    HttpStatusCode.TooManyRequests);

            // Record this request
            bucket.RecordRequest(now);
        }

        return AuthResult.Success(deviceId);
    }

    private static string? GetDeviceId(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("x-device-id", out var values))
            return values.FirstOrDefault();
        return null;
    }

    private static bool SecureCompare(string? a, string? b)
    {
        if (a == null || b == null)
            return a == b;

        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }

    /// <summary>
    /// Create unauthorized response.
    /// </summary>
    public static HttpResponseData CreateErrorResponse(HttpRequestData req, AuthResult result)
    {
        var res = req.CreateResponse(result.StatusCode);
        res.WriteString(result.ErrorMessage ?? "Unauthorized");
        return res;
    }

    private sealed class RateLimitBucket
    {
        private readonly List<DateTimeOffset> _requests = new();

        public void RecordRequest(DateTimeOffset now)
        {
            _requests.Add(now);
        }

        public int GetMinuteCount(DateTimeOffset now)
        {
            var cutoff = now.AddMinutes(-1);
            return _requests.Count(r => r > cutoff);
        }

        public int GetHourCount(DateTimeOffset now)
        {
            var cutoff = now.AddHours(-1);
            return _requests.Count(r => r > cutoff);
        }

        public void CleanOldEntries(DateTimeOffset now)
        {
            var cutoff = now.AddHours(-1);
            _requests.RemoveAll(r => r <= cutoff);
        }
    }
}

/// <summary>
/// Result of authentication/authorization check.
/// </summary>
public sealed record AuthResult(
    bool IsValid,
    string? ErrorMessage,
    HttpStatusCode StatusCode,
    string? DeviceId)
{
    public static AuthResult Success(string? deviceId = null) =>
        new(true, null, HttpStatusCode.OK, deviceId);

    public static AuthResult Fail(string message, HttpStatusCode status = HttpStatusCode.Unauthorized) =>
        new(false, message, status, null);
}
