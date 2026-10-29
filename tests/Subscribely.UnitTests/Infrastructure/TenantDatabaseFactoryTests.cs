using Microsoft.Extensions.Options;
using Subscribely.Infrastructure.Tenancy;

namespace Subscribely.UnitTests.Infrastructure;

public sealed class TenantDatabaseFactoryTests
{
    private const string Template = "Host=db;Database=subscribely_t_{slug};Username=app;Password=pw";

    private static TenantDatabaseFactory Create(string template = Template) =>
        new(Options.Create(new TenantDatabaseOptions { ConnectionTemplate = template }));

    [Fact]
    public void ConnectionStringFor_ReplacesSlugAndHyphens() =>
        Create().ConnectionStringFor("acme-corp")
            .ShouldBe("Host=db;Database=subscribely_t_acme_corp;Username=app;Password=pw");

    [Fact]
    public void DatabaseNameFor_ReadsDatabaseFromTemplate() =>
        Create().DatabaseNameFor("acme").ShouldBe("subscribely_t_acme");

    [Theory]
    [InlineData("ab")]
    [InlineData("Acme")]
    [InlineData("acme;Password=x")]
    [InlineData("acme corp")]
    [InlineData("a_b_c")]
    [InlineData("")]
    public void ConnectionStringFor_InvalidSlug_Throws(string slug) =>
        Should.Throw<ArgumentException>(() => Create().ConnectionStringFor(slug));

    [Fact]
    public void ConnectionStringFor_TemplateWithoutPlaceholder_Throws() =>
        Should.Throw<InvalidOperationException>(() => Create("Host=db;Database=fixed").ConnectionStringFor("acme"));

    [Theory]
    [InlineData("abc", true)]
    [InlineData("acme-2026", true)]
    [InlineData("x", false)]
    [InlineData(null, false)]
    public void IsValidSlug(string? slug, bool expected) =>
        TenantDatabaseFactory.IsValidSlug(slug).ShouldBe(expected);
}
