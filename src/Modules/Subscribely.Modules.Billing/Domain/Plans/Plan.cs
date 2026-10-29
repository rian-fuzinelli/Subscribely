using Subscribely.Infrastructure.Auditing;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Domain.Plans;

internal enum PlanStatus
{
    Active,
    Archived,
}

/// <summary>Termos comerciais de uma versão de plano.</summary>
internal sealed record PlanTerms(
    string Name,
    long MonthlyPriceCents,
    long AnnualPriceCents,
    int RequestQuotaMonthly,
    int MaxSeats)
{
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 60)
        {
            return Error.Validation("plan-name-invalid", "Plan name must have 1 to 60 characters.");
        }

        if (MonthlyPriceCents < 0 || AnnualPriceCents < 0)
        {
            return Error.Validation("plan-price-invalid", "Prices cannot be negative.");
        }

        if (AnnualPriceCents > checked(MonthlyPriceCents * 12))
        {
            return Error.Unprocessable("annual-price-invalid", "Annual price cannot exceed 12 times the monthly price.");
        }

        if (RequestQuotaMonthly <= 0 || MaxSeats <= 0)
        {
            return Error.Validation("plan-limits-invalid", "Request quota and max seats must be positive.");
        }

        return Result.Success();
    }
}

/// <summary>Plano comercializado. Alterações criam uma nova <see cref="PlanVersion"/> imutável.</summary>
[Auditable]
internal sealed class Plan : Entity
{
    private readonly List<PlanVersion> _versions = [];

    private Plan()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public PlanStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<PlanVersion> Versions => _versions;

    public PlanVersion CurrentVersion => _versions.MaxBy(v => v.Version)
        ?? throw new InvalidOperationException($"Plan {Code} has no versions loaded.");

    public static Result<Plan> Create(string code, PlanTerms terms, DateTimeOffset now)
    {
        var validation = terms.Validate();
        if (!validation.IsSuccess)
        {
            return validation.Error;
        }

        var plan = new Plan { Code = code, Status = PlanStatus.Active, CreatedAt = now };
        plan._versions.Add(PlanVersion.Create(plan.Id, 1, terms, now));
        return plan;
    }

    public Result<PlanVersion> Revise(PlanTerms terms, DateTimeOffset now)
    {
        var validation = terms.Validate();
        if (!validation.IsSuccess)
        {
            return validation.Error;
        }

        var version = PlanVersion.Create(Id, CurrentVersion.Version + 1, terms, now);
        _versions.Add(version);
        return version;
    }

    public void Archive() => Status = PlanStatus.Archived;
}

[Auditable]
internal sealed class PlanVersion
{
    private PlanVersion()
    {
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public int Version { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public long MonthlyPriceCents { get; private set; }

    public long AnnualPriceCents { get; private set; }

    public int RequestQuotaMonthly { get; private set; }

    public int MaxSeats { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PlanVersion Create(Guid planId, int version, PlanTerms terms, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        PlanId = planId,
        Version = version,
        Name = terms.Name.Trim(),
        MonthlyPriceCents = terms.MonthlyPriceCents,
        AnnualPriceCents = terms.AnnualPriceCents,
        RequestQuotaMonthly = terms.RequestQuotaMonthly,
        MaxSeats = terms.MaxSeats,
        CreatedAt = now,
    };
}
