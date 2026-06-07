using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

namespace DebtManager.Cloud.Security;

public static class ApiKeyAuth
{
    public static bool Check(HttpRequestData req)
    {
        var expected = Environment.GetEnvironmentVariable("SyncApiKey");
        if (string.IsNullOrWhiteSpace(expected))
            return true; // dev-friendly; set key to enforce

        if (!req.Headers.TryGetValues("x-sync-key", out var values))
            return false;

        var actual = values.FirstOrDefault();
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    public static HttpResponseData Unauthorized(HttpRequestData req)
    {
        var res = req.CreateResponse(HttpStatusCode.Unauthorized);
        res.WriteString("Unauthorized");
        return res;
    }
}
