using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Subscribely.Modules.Billing.Domain.Plans;

namespace Subscribely.Modules.Billing.Persistence;

/// <summary>Cria os planos de demonstração em Development (idempotente).</summary>
internal sealed class DevelopmentSeed(IServiceProvider services, IHostEnvironment environment, TimeProvider time) : IHostedService
{
    private static readonly (string Code, PlanTerms Terms)[] Plans =
    [
        ("starter", new PlanTerms("Starter", 4_900, 49_000, 10_000, 3)),
        ("pro", new PlanTerms("Pro", 14_900, 149_000, 100_000, 10)),
        ("business", new PlanTerms("Business", 49_900, 499_000, 1_000_000, 50)),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var existing = await db.Plans.Select(p => p.Code).ToListAsync(cancellationToken);
        foreach (var (code, terms) in Plans.Where(p => !existing.Contains(p.Code)))
        {
            db.Plans.Add(Plan.Create(code, terms, time.GetUtcNow()).Value);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
