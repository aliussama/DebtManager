using Azure.Data.Tables;

namespace DebtManager.Cloud.Storage;

public sealed class TableClientFactory
{
    private readonly string _connectionString;
    private readonly string _tableName;

    public TableClientFactory()
    {
        _connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage is missing.");
        _tableName = Environment.GetEnvironmentVariable("EventLogTableName") ?? "EventLog";
    }

    public async Task<TableClient> GetAsync(CancellationToken ct)
    {
        var client = new TableClient(_connectionString, _tableName);
        await client.CreateIfNotExistsAsync(ct);
        return client;
    }
}
