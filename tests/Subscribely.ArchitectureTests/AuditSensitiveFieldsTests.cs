using System.Reflection;
using Subscribely.Infrastructure.Auditing;
using Subscribely.Modules.Audit;
using Subscribely.Modules.Billing;
using Subscribely.Modules.Metering;
using Subscribely.Modules.Notifications;
using Subscribely.Modules.Payments;
using Subscribely.Modules.Projects;
using Subscribely.Modules.Tenancy;

namespace Subscribely.ArchitectureTests;

/// <summary>Toda propriedade sensível de entidade auditada precisa de [NotAudited] (nunca vai para o JSON da trilha).</summary>
public sealed class AuditSensitiveFieldsTests
{
    private static readonly string[] SensitiveMarkers = ["hash", "token", "secret", "password"];

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(TenancyModule).Assembly,
        typeof(BillingModule).Assembly,
        typeof(PaymentsModule).Assembly,
        typeof(MeteringModule).Assembly,
        typeof(NotificationsModule).Assembly,
        typeof(AuditModule).Assembly,
        typeof(ProjectsModule).Assembly,
    ];

    [Fact]
    public void AuditableEntities_MarkSensitivePropertiesAsNotAudited()
    {
        var violations = FindViolations(ModuleAssemblies.SelectMany(a => a.GetTypes()));

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Rule_DetectsUnmarkedSensitiveProperty() =>
        FindViolations([typeof(LeakyEntity)]).ShouldBe([$"{typeof(LeakyEntity).FullName}.PasswordHash"]);

    private static List<string> FindViolations(IEnumerable<Type> types) =>
        types
            .Where(t => t.GetCustomAttribute<AuditableAttribute>() is not null)
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => SensitiveMarkers.Any(m => p.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
                .Where(p => p.GetCustomAttribute<NotAuditedAttribute>() is null)
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

    [Auditable]
    private sealed class LeakyEntity
    {
        public string PasswordHash { get; set; } = string.Empty;

        [NotAudited]
        public string TokenHash { get; set; } = string.Empty;
    }
}
