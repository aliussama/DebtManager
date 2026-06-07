namespace DebtManager.Application.Projections;

/// <summary>
/// Policy constants for projection caching and snapshot creation.
/// </summary>
public static class ProjectionCachePolicies
{
    /// <summary>Minimum events since last snapshot before a new snapshot is created.</summary>
    public const int SnapshotEventThreshold = 2000;

    /// <summary>Minimum time since last snapshot before a new snapshot is created.</summary>
    public static readonly TimeSpan SnapshotTimeThreshold = TimeSpan.FromDays(1);

    /// <summary>Number of snapshots to keep per projection when pruning.</summary>
    public const int PruneKeepLastN = 3;

    /// <summary>Schema versions for each supported projection snapshot.</summary>
    public static class SchemaVersions
    {
        public const int CashLedgerState = 1;
        public const int BankImportState = 1;
        public const int AssetsState = 1;
        public const int PortfolioState = 1;
        public const int TaxState = 1;
    }

    /// <summary>Projections that are eligible for snapshotting.</summary>
    public static readonly HashSet<string> SnapshottableProjections = new()
    {
        nameof(SchemaVersions.CashLedgerState),
        nameof(SchemaVersions.BankImportState),
        nameof(SchemaVersions.AssetsState),
        nameof(SchemaVersions.PortfolioState),
        nameof(SchemaVersions.TaxState)
    };

    public static int GetSchemaVersion(string projectionName) => projectionName switch
    {
        nameof(SchemaVersions.CashLedgerState) => SchemaVersions.CashLedgerState,
        nameof(SchemaVersions.BankImportState) => SchemaVersions.BankImportState,
        nameof(SchemaVersions.AssetsState) => SchemaVersions.AssetsState,
        nameof(SchemaVersions.PortfolioState) => SchemaVersions.PortfolioState,
        nameof(SchemaVersions.TaxState) => SchemaVersions.TaxState,
        _ => 0
    };
}
