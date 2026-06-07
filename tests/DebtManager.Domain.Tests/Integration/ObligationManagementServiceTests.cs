using DebtManager.Application.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Simulation;
using Xunit;

namespace DebtManager.Domain.Tests.Integration;

public class ObligationManagementServiceTests
{
    [Fact]
    public async Task GetObligationSnapshotAsync_WithValidObligation_ReturnsSnapshot()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(Array.Empty<EventEnvelope>());
        var service = ApplicationServiceFactory.CreateWithSampleRules(eventStore);

        var obligationId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // Create an obligation
        var created = new ObligationCreated(
            ObligationId: obligationId,
            Name: "Test Loan",
            ObligationType: "Loan",
            Principal: new Money(100_000m, Currency.EGP),
            StartDate: new DateOnly(2025, 1, 1),
            CurrencyCode: "EGP"
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(obligationId),
            nameof(ObligationCreated),
            DateTimeOffset.UtcNow,
            created.StartDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            System.Text.Json.JsonSerializer.Serialize(created, DomainJson.Options)
        );

        await eventStore.AppendAsync(envelope, CancellationToken.None);

        // Act
        var snapshot = await service.GetObligationSnapshotAsync(
            obligationId,
            new DateOnly(2025, 6, 15),
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(obligationId, snapshot.ObligationId);
        Assert.Equal("Test Loan", snapshot.Name);
        Assert.Equal(100_000m, snapshot.Principal.Amount);
        Assert.False(snapshot.IsClosed);
    }

    [Fact]
    public async Task GetObligationSnapshotAsync_WithNonExistentObligation_ThrowsException()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(Array.Empty<EventEnvelope>());
        var service = ApplicationServiceFactory.CreateWithSampleRules(eventStore);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetObligationSnapshotAsync(
                Guid.NewGuid(),
                new DateOnly(2025, 6, 15),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task GetPortfolioDashboardAsync_WithMultipleObligations_ReturnsAggregatedData()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(Array.Empty<EventEnvelope>());
        var service = ApplicationServiceFactory.CreateWithSampleRules(eventStore);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // Create multiple obligations
        for (var i = 0; i < 3; i++)
        {
            var obligationId = Guid.NewGuid();
            var created = new ObligationCreated(
                ObligationId: obligationId,
                Name: $"Loan {i + 1}",
                ObligationType: "Loan",
                Principal: new Money((i + 1) * 50_000m, Currency.EGP),
                StartDate: new DateOnly(2025, 1, 1),
                CurrencyCode: "EGP"
            );

            var envelope = new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(obligationId),
                nameof(ObligationCreated),
                DateTimeOffset.UtcNow,
                created.StartDate,
                actorUserId,
                deviceId,
                Guid.NewGuid(),
                null,
                1,
                System.Text.Json.JsonSerializer.Serialize(created, DomainJson.Options)
            );

            await eventStore.AppendAsync(envelope, CancellationToken.None);
        }

        // Act
        var dashboard = await service.GetPortfolioDashboardAsync(
            new DateOnly(2025, 6, 15),
            Currency.EGP,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(dashboard);
        Assert.Equal(3, dashboard.TotalObligations);
        Assert.Equal(3, dashboard.ActiveObligations);
        Assert.Equal(0, dashboard.ClosedObligations);

        // Total principal: 50k + 100k + 150k = 300k
        Assert.Equal(300_000m, dashboard.TotalPrincipal.Amount);
    }

    [Fact]
    public async Task GetPaymentProjectionsAsync_WithDateRange_ReturnsProjections()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(Array.Empty<EventEnvelope>());
        var service = ApplicationServiceFactory.CreateWithSampleRules(eventStore);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var created = new ObligationCreated(
            ObligationId: obligationId,
            Name: "Test Loan",
            ObligationType: "Loan",
            Principal: new Money(100_000m, Currency.EGP),
            StartDate: new DateOnly(2025, 1, 1),
            CurrencyCode: "EGP"
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(obligationId),
            nameof(ObligationCreated),
            DateTimeOffset.UtcNow,
            created.StartDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            System.Text.Json.JsonSerializer.Serialize(created, DomainJson.Options)
        );

        await eventStore.AppendAsync(envelope, CancellationToken.None);

        // Act
        var projections = await service.GetPaymentProjectionsAsync(
            new DateOnly(2025, 6, 1),
            new DateOnly(2025, 12, 31),
            Currency.EGP,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(projections);
        // Without schedules, there are no installments to project
        Assert.Empty(projections);
    }

    [Fact]
    public async Task GetPayoffProjectionsAsync_WithActiveObligations_ReturnsProjections()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(Array.Empty<EventEnvelope>());
        var service = ApplicationServiceFactory.CreateWithSampleRules(eventStore);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var created = new ObligationCreated(
            ObligationId: obligationId,
            Name: "Test Loan",
            ObligationType: "Loan",
            Principal: new Money(100_000m, Currency.EGP),
            StartDate: new DateOnly(2025, 1, 1),
            CurrencyCode: "EGP"
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(obligationId),
            nameof(ObligationCreated),
            DateTimeOffset.UtcNow,
            created.StartDate,
            actorUserId,
            deviceId,
            Guid.NewGuid(),
            null,
            1,
            System.Text.Json.JsonSerializer.Serialize(created, DomainJson.Options)
        );

        await eventStore.AppendAsync(envelope, CancellationToken.None);

        // Act
        var projections = await service.GetPayoffProjectionsAsync(
            Currency.EGP,
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(projections);
        Assert.Single(projections);

        var projection = projections[0];
        Assert.Equal(obligationId, projection.ObligationId);
        Assert.Equal("Test Loan", projection.Name);
        Assert.Equal(100_000m, projection.CurrentBalance.Amount);
    }

    [Fact]
    public void ApplicationServiceFactory_CreateWithSampleRules_LoadsAllPacks()
    {
        // Arrange
        var eventStore = new InMemoryEventStore(Array.Empty<EventEnvelope>());

        // Act
        var service = ApplicationServiceFactory.CreateWithSampleRules(eventStore);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void ApplicationServiceFactory_CreateNoOpRuleEngine_ReturnsNoOp()
    {
        // Act
        var engine = ApplicationServiceFactory.CreateNoOpRuleEngine();

        // Assert
        Assert.NotNull(engine);
        Assert.IsType<NoOpRuleEngine>(engine);
    }

    [Fact]
    public void ApplicationServiceFactory_CreateTracingRuleEngine_WrapsInner()
    {
        // Arrange
        var inner = new NoOpRuleEngine();

        // Act
        var tracing = ApplicationServiceFactory.CreateTracingRuleEngine(inner);

        // Assert
        Assert.NotNull(tracing);
        Assert.IsType<TracingRuleEngine>(tracing);
    }
}
