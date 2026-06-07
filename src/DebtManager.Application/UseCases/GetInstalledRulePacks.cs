using DebtManager.Infrastructure.Rules;

namespace DebtManager.Application.UseCases;

/// <summary>
/// DTO for an installed rule pack with version information.
/// </summary>
public sealed record InstalledRulePackDto(
    string PackId,
    string Name,
    string? Description,
    string VersionLabel,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    int RulesCount
);

/// <summary>
/// Query handler to retrieve all installed rule packs.
/// </summary>
public sealed class GetInstalledRulePacksHandler
{
    private readonly IRulePackRepository _repository;

    public GetInstalledRulePacksHandler(IRulePackRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<InstalledRulePackDto>> HandleAsync(CancellationToken ct)
    {
        var packs = await _repository.GetAllAsync(ct);

        return packs
            .Select(p => new InstalledRulePackDto(
                PackId: p.RulePackId,
                Name: p.Name,
                Description: p.Description,
                VersionLabel: p.VersionLabel,
                EffectiveFrom: p.EffectiveFrom,
                EffectiveTo: p.EffectiveTo,
                Status: p.Status,
                RulesCount: p.RulesCount
            ))
            .ToList();
    }
}
