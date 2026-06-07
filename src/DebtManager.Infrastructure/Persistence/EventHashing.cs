using System.Security.Cryptography;
using System.Text;
using DebtManager.Domain.Events;

namespace DebtManager.Infrastructure.Persistence;

public static class EventHashing
{
    // Canonical representation of what we hash (stable across machines)
    public static string ComputeHashHex(string? prevHashHex, EventEnvelope e)
    {
        prevHashHex ??= "";

        // IMPORTANT: canonical order, explicit separators, culture-invariant formatting
        var canonical =
            $"prev={prevHashHex}\n" +
            $"event_id={e.EventId.Value:N}\n" +
            $"stream_id={e.StreamId.Value:N}\n" +
            $"event_type={e.EventType}\n" +
            $"occurred_at={e.OccurredAt.ToString("O")}\n" +
            $"effective_date={e.EffectiveDate:yyyy-MM-dd}\n" +
            $"actor_user_id={e.ActorUserId:N}\n" +
            $"device_id={e.DeviceId:N}\n" +
            $"correlation_id={e.CorrelationId:N}\n" +
            $"causation_event_id={(e.CausationEventId.HasValue ? e.CausationEventId.Value.ToString("N") : "")}\n" +
            $"payload_schema_version={e.PayloadSchemaVersion}\n" +
            $"payload_json={NormalizeJson(e.PayloadJson)}\n";

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return ToHex(hash);
    }

    // Minimal normalization: trim. (Full canonical JSON comes later.)
    private static string NormalizeJson(string json)
        => (json ?? "").Trim();

    public static bool SlowEqualsHex(string a, string b)
    {
        // constant-time comparison
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var t in bytes)
            sb.Append(t.ToString("x2"));
        return sb.ToString();
    }
}
