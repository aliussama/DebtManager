using Microsoft.Data.Sqlite;

namespace DebtManager.Infrastructure.Rules;

public sealed record RulePackVersionRow(
    Guid RulePackVersionId,
    string RulePackId,
    string VersionLabel,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string RulesJson
);

public interface IRulePackRepository
{
    Task UpsertPackAsync(string rulePackId, string name, string? description, CancellationToken ct);
    Task AddVersionAsync(RulePackVersionRow version, CancellationToken ct);
    Task<RulePackVersionRow?> GetActiveVersionAsync(string rulePackId, DateOnly asOfDate, CancellationToken ct);
}

public sealed class SqliteRulePackRepository : IRulePackRepository
{
    private readonly Persistence.SqliteConnectionFactory _factory;

    public SqliteRulePackRepository(Persistence.SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task UpsertPackAsync(string rulePackId, string name, string? description, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new Persistence.SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO rule_packs(rule_pack_id, name, description)
VALUES ($id, $name, $desc)
ON CONFLICT(rule_pack_id) DO UPDATE SET
  name = excluded.name,
  description = excluded.description;
""";
        cmd.Parameters.AddWithValue("$id", rulePackId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$desc", (object?)description ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddVersionAsync(RulePackVersionRow v, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new Persistence.SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
INSERT INTO rule_pack_versions(
  rule_pack_version_id, rule_pack_id, version_label,
  effective_from, effective_to, status, rules_json
)
VALUES(
  $vid, $pid, $label,
  $from, $to, $status, $json
);
""";
        cmd.Parameters.AddWithValue("$vid", v.RulePackVersionId.ToString());
        cmd.Parameters.AddWithValue("$pid", v.RulePackId);
        cmd.Parameters.AddWithValue("$label", v.VersionLabel);
        cmd.Parameters.AddWithValue("$from", v.EffectiveFrom.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$to", (object?)(v.EffectiveTo?.ToString("yyyy-MM-dd")) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", v.Status);
        cmd.Parameters.AddWithValue("$json", v.RulesJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<RulePackVersionRow?> GetActiveVersionAsync(string rulePackId, DateOnly asOfDate, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new Persistence.SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
SELECT rule_pack_version_id, rule_pack_id, version_label,
       effective_from, effective_to, status, rules_json
FROM rule_pack_versions
WHERE rule_pack_id = $pid
  AND effective_from <= $asof
  AND (effective_to IS NULL OR effective_to >= $asof)
  AND status = 'active'
ORDER BY effective_from DESC
LIMIT 1;
""";
        cmd.Parameters.AddWithValue("$pid", rulePackId);
        cmd.Parameters.AddWithValue("$asof", asOfDate.ToString("yyyy-MM-dd"));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;

        var vid = Guid.Parse(r["rule_pack_version_id"].ToString()!);
        var pid = r["rule_pack_id"].ToString()!;
        var label = r["version_label"].ToString()!;
        var from = DateOnly.Parse(r["effective_from"].ToString()!);

        var toStr = r["effective_to"]?.ToString();
        DateOnly? to = string.IsNullOrWhiteSpace(toStr) ? null : DateOnly.Parse(toStr);

        var status = r["status"].ToString()!;
        var json = r["rules_json"].ToString()!;

        return new RulePackVersionRow(vid, pid, label, from, to, status, json);
    }
}
