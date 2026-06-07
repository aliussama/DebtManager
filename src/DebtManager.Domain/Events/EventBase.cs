namespace DebtManager.Domain.Events;

public interface IDomainEvent
{
    DateOnly EffectiveDate { get; }
}

public abstract record DomainEvent(DateOnly EffectiveDate) : IDomainEvent;
