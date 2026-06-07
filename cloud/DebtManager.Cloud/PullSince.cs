using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using DebtManager.Cloud.Security;
using DebtManager.Cloud.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DebtManager.Cloud;

public sealed class PullSince
{
    private readonly TableClientFactory _tables = new();

    public sealed record SyncEventDto(
        string EventId,
        string StreamId,
        string EventType,
        string OccurredAt,
        string EffectiveDate,
        string ActorUserId,
        string DeviceId,
        string CorrelationId,
        string? CausationEventId,
        int PayloadSchemaVersion,
        string PayloadJson,
        string? PrevHash,
        string? Hash
    );

    public sealed record PullResponse(string Cursor, List<SyncEventDto> Events);

    [Function("PullSince")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/vaults/{vaultId}/events")] HttpRequestData req,
        string vaultId,
        FunctionContext ctx)
    {
        if (!ApiKeyAuth.Check(req))
            return ApiKeyAuth.Unauthorized(req);

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var since = query.Get("since"); // rowkey cursor
        var limitStr = query.Get("limit");
        var limit = int.TryParse(limitStr, out var n) ? Math.Clamp(n, 1, 500) : 200;

        var table = await _tables.GetAsync(CancellationToken.None);

        // filter: PartitionKey eq vaultId and RowKey gt sinceCursor (if provided)
        string filter = string.IsNullOrWhiteSpace(since)
            ? $"PartitionKey eq '{vaultId}'"
            : $"PartitionKey eq '{vaultId}' and RowKey gt '{since}'";

        var events = new List<SyncEventDto>();
        string newCursor = since ?? "";

        await foreach (var ent in table.QueryAsync<EventEntity>(filter: filter, maxPerPage: limit))
        {
            events.Add(new SyncEventDto(
                ent.EventId,
                ent.StreamId,
                ent.EventType,
                ent.OccurredAt,
                ent.EffectiveDate,
                ent.ActorUserId,
                ent.DeviceId,
                ent.CorrelationId,
                ent.CausationEventId,
                ent.PayloadSchemaVersion,
                ent.PayloadJson,
                ent.PrevHash,
                ent.Hash
            ));

            newCursor = ent.RowKey;
            if (events.Count >= limit) break;
        }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new PullResponse(newCursor, events));
        return res;
    }
}
