using Azure;
using Azure.Data.Tables;
using DebtManager.Cloud.Security;
using DebtManager.Cloud.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace DebtManager.Cloud;

public sealed class PushBatch
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

    public sealed record PushBatchRequest(string DeviceId, List<SyncEventDto>? Events);
    public sealed record PushBatchResponse(int Accepted, int AlreadyPresent);

    [Function("PushBatch")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/vaults/{vaultId}/events:batch")] HttpRequestData req,
        string vaultId,
        FunctionContext ctx)
    {
        if (!ApiKeyAuth.Check(req))
            return ApiKeyAuth.Unauthorized(req);

        var json = await new StreamReader(req.Body).ReadToEndAsync();

        ctx.GetLogger("PushBatch").LogInformation("Raw JSON: {json}", json);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        PushBatchRequest? body;
        try
        {
            body = JsonSerializer.Deserialize<PushBatchRequest>(json, options);
        }
        catch (JsonException)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.WriteString("Invalid JSON body.");
            return bad;
        }

        if (body is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.WriteString("Missing JSON body.");
            return bad;
        }

        var events = body.Events ?? new List<SyncEventDto>();

        ctx.GetLogger("PushBatch").LogInformation("Parsed events count: {count}", events.Count);

        var table = await _tables.GetAsync(CancellationToken.None);

        var accepted = 0;
        var already = 0;

        foreach (var e in events)
        {
            // RowKey sortable: ticks + "_" + eventId without dashes
            var occurred = DateTimeOffset.Parse(e.OccurredAt);
            var rowKey = $"{occurred.UtcTicks:D19}_{e.EventId.Replace("-", "")}";

            var entity = new EventEntity
            {
                PartitionKey = vaultId,
                RowKey = rowKey,

                EventId = e.EventId,
                StreamId = e.StreamId,
                EventType = e.EventType,
                OccurredAt = e.OccurredAt,
                EffectiveDate = e.EffectiveDate,
                ActorUserId = e.ActorUserId,
                DeviceId = e.DeviceId,
                CorrelationId = e.CorrelationId,
                CausationEventId = e.CausationEventId,
                PayloadSchemaVersion = e.PayloadSchemaVersion,
                PayloadJson = e.PayloadJson,
                PrevHash = e.PrevHash,
                Hash = e.Hash
            };

            try
            {
                await table.AddEntityAsync(entity, CancellationToken.None);
                accepted++;
                ctx.GetLogger("PushBatch").LogInformation("Inserted RowKey={rowKey}", rowKey);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                already++;
                ctx.GetLogger("PushBatch").LogInformation("Already exists RowKey={rowKey}", rowKey);
            }
            catch (RequestFailedException ex)
            {
                ctx.GetLogger("PushBatch").LogError(ex, "Storage error Status={status} RowKey={rowKey}", ex.Status, rowKey);
                var err = req.CreateResponse(HttpStatusCode.InternalServerError);
                await err.WriteStringAsync($"Storage error: {ex.Status} {ex.Message}");
                return err;
            }
        }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new PushBatchResponse(accepted, already));
        return res;
    }
}
