using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;
using Xunit;

namespace DebtManager.Integration.Tests;

/// <summary>
/// Integration test verifying the rule pack UI flow:
/// 1. Create obligation
/// 2. Install sample pack
/// 3. Assign pack to obligation
/// 4. Verify assignment event exists
/// </summary>
public class RulePackUiFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteRulePackRepository _rulePackRepo;

    public RulePackUiFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"RulePackUiFlowTest_{Guid.NewGuid()}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
        _rulePackRepo = new SqliteRulePackRepository(_factory);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task FullRulePackFlow_CreateObligation_InstallPack_Assign_VerifyEvent()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var installHandler = new InstallRulePackHandler(_rulePackRepo);
        var assignHandler = new AssignRulePackToObligationHandler(_eventStore);
        var getPacksHandler = new GetInstalledRulePacksHandler(_rulePackRepo);
        var getAssignmentHandler = new GetRulePackAssignmentHandler(_eventStore, _rulePackRepo);

        // Step 1: Create an obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "Test Loan",
                "Loan",
                50000m,
                "EGP",
                DateOnly.FromDateTime(DateTime.Today)
            ),
            actorUserId,
            deviceId,
            CancellationToken.None
        );

        // Step 2: Install sample rule pack (basic_loan)
        var loader = new RulePackLoader();
        var sampleJson = SampleRulePacks.BasicLoan;
        var pack = loader.Load(sampleJson);
        var version = pack.Versions.First();

        await installHandler.HandleAsync(
            new InstallRulePackCommand(
                RulePackId: pack.PackId,
                Name: pack.DisplayName,
                Description: "Sample rule pack for testing",
                VersionLabel: version.VersionLabel,
                EffectiveFrom: version.EffectiveFrom,
                EffectiveTo: version.EffectiveTo,
                Status: version.Status,
                RulesJson: sampleJson
            ),
            CancellationToken.None
        );

        // Verify pack is installed
        var installedPacks = await getPacksHandler.HandleAsync(CancellationToken.None);
        Assert.Contains(installedPacks, p => p.PackId == pack.PackId);

        // Step 3: Assign pack to obligation
        var effectiveDate = DateOnly.FromDateTime(DateTime.Today);
        await assignHandler.HandleAsync(
            new AssignRulePackToObligationCommand(
                ObligationId: obligationId,
                RulePackId: pack.PackId,
                EffectiveDate: effectiveDate
            ),
            actorUserId,
            deviceId,
            CancellationToken.None
        );

        // Step 4: Verify assignment exists in event stream
        var events = await _eventStore.ReadStreamAsync(
            new StreamId(obligationId),
            upTo: null,
            CancellationToken.None
        );

        var assignmentEvent = events.FirstOrDefault(e => 
            e.EventType == nameof(RulePackAssignedToObligation));
        
        Assert.NotNull(assignmentEvent);

        var assignment = JsonSerializer.Deserialize<RulePackAssignedToObligation>(
            assignmentEvent.PayloadJson, DomainJson.Options);
        
        Assert.NotNull(assignment);
        Assert.Equal(obligationId, assignment.ObligationId);
        Assert.Equal(pack.PackId, assignment.RulePackId);
        Assert.Equal(effectiveDate, assignment.EffectiveDate);

        // Step 5: Verify GetRulePackAssignmentHandler returns the assignment
        var currentAssignment = await getAssignmentHandler.HandleAsync(
            obligationId, CancellationToken.None);
        
        Assert.NotNull(currentAssignment);
        Assert.Equal(pack.PackId, currentAssignment.PackId);
        Assert.Equal(pack.DisplayName, currentAssignment.PackName);
        Assert.Equal(effectiveDate, currentAssignment.EffectiveDate);
    }

    [Fact]
    public async Task InstallAllSamplePacks_AllPacksAvailable()
    {
        // Arrange
        var installHandler = new InstallRulePackHandler(_rulePackRepo);
        var getPacksHandler = new GetInstalledRulePacksHandler(_rulePackRepo);
        var loader = new RulePackLoader();

        // Act - Install all sample packs
        foreach (var (_, json) in SampleRulePacks.All)
        {
            var pack = loader.Load(json);
            var version = pack.Versions.First();

            await installHandler.HandleAsync(
                new InstallRulePackCommand(
                    RulePackId: pack.PackId,
                    Name: pack.DisplayName,
                    Description: $"Sample {pack.DisplayName}",
                    VersionLabel: version.VersionLabel,
                    EffectiveFrom: version.EffectiveFrom,
                    EffectiveTo: version.EffectiveTo,
                    Status: version.Status,
                    RulesJson: json
                ),
                CancellationToken.None
            );
        }

        // Assert
        var installedPacks = await getPacksHandler.HandleAsync(CancellationToken.None);
        
        Assert.Equal(SampleRulePacks.All.Count, installedPacks.Count);
        Assert.Contains(installedPacks, p => p.PackId == "basic_loan");
        Assert.Contains(installedPacks, p => p.PackId == "credit_card_standard");
        Assert.Contains(installedPacks, p => p.PackId == "mortgage_standard");
        Assert.Contains(installedPacks, p => p.PackId == "university_tuition");
    }
}
