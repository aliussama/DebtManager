using DebtManager.Infrastructure.Rules;

namespace DebtManager.Application.UseCases;

public sealed record InstallRulePackCommand(
    string RulePackId,
    string Name,
    string? Description,
    string VersionLabel,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string RulesJson
);

public sealed class InstallRulePackHandler
{
    private readonly IRulePackRepository _repo;

    public InstallRulePackHandler(IRulePackRepository repo)
    {
        _repo = repo;
    }

    public async Task HandleAsync(InstallRulePackCommand cmd, CancellationToken ct)
    {
        await _repo.UpsertPackAsync(cmd.RulePackId, cmd.Name, cmd.Description, ct);

        await _repo.AddVersionAsync(new RulePackVersionRow(
            RulePackVersionId: Guid.NewGuid(),
            RulePackId: cmd.RulePackId,
            VersionLabel: cmd.VersionLabel,
            EffectiveFrom: cmd.EffectiveFrom,
            EffectiveTo: cmd.EffectiveTo,
            Status: cmd.Status,
            RulesJson: cmd.RulesJson
        ), ct);
    }
}
