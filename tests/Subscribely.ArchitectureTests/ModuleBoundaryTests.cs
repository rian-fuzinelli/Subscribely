using System.Reflection;
using NetArchTest.Rules;
using Subscribely.Modules.Audit;
using Subscribely.Modules.Billing;
using Subscribely.Modules.Metering;
using Subscribely.Modules.Notifications;
using Subscribely.Modules.Payments;
using Subscribely.Modules.Projects;
using Subscribely.Modules.Tenancy;
using Subscribely.SharedKernel;

namespace Subscribely.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private const string ModulesNamespace = "Subscribely.Modules";

    public static TheoryData<string> Modules =>
    [
        typeof(TenancyModule).Assembly.GetName().Name!,
        typeof(BillingModule).Assembly.GetName().Name!,
        typeof(PaymentsModule).Assembly.GetName().Name!,
        typeof(MeteringModule).Assembly.GetName().Name!,
        typeof(NotificationsModule).Assembly.GetName().Name!,
        typeof(AuditModule).Assembly.GetName().Name!,
        typeof(ProjectsModule).Assembly.GetName().Name!,
    ];

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_ExposesOnlyContractsAndRegistration(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var leaked = Types.InAssembly(assembly)
            .That().ArePublic()
            .And().DoNotResideInNamespaceEndingWith(".Contracts")
            .And().DoNotHaveNameEndingWith("Module")
            .GetTypes()
            .Select(t => t.FullName)
            .ToList();

        leaked.ShouldBeEmpty($"Only Contracts and the module registration may be public in {assemblyName}.");
    }

    [Fact]
    public void BuildingBlocks_DoNotDependOnModules()
    {
        var result = Types.InAssemblies([typeof(Result).Assembly, Assembly.Load("Subscribely.Infrastructure")])
            .ShouldNot().HaveDependencyOn(ModulesNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_DoesNotDependOnApiHost(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(assemblyName))
            .ShouldNot().HaveDependencyOn("Subscribely.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
