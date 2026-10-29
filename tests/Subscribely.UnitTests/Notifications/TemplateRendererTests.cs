using Subscribely.Modules.Notifications.Email;

namespace Subscribely.UnitTests.Notifications;

public sealed class TemplateRendererTests
{
    private static readonly string[] Templates =
    [
        "trial-ending", "invoice-issued", "invoice-paid", "payment-failed", "subscription-suspended",
        "quota-80", "quota-100", "user-invitation", "password-reset",
    ];

    private static readonly Dictionary<string, string> AllValues = new()
    {
        ["company"] = "Acme",
        ["trialEndsAt"] = "01/02/2026",
        ["number"] = "SUB-2026-000001",
        ["total"] = "R$ 49,00",
        ["issuedAt"] = "01/02/2026 às 09:00",
        ["paidAt"] = "01/02/2026 às 09:01",
        ["reason"] = "cartão recusado",
        ["nextAttempt"] = "Vamos tentar de novo amanhã.",
        ["period"] = "02/2026",
        ["limit"] = "1.000",
        ["resetsAt"] = "01/03/2026 às 00:00",
        ["expiresAt"] = "04/02/2026 às 09:00",
        ["link"] = "https://portal.test/x?token=abc",
    };

    private readonly TemplateRenderer _renderer = new();

    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void EveryTemplate_HasSubjectHtmlAndText_InPortuguese(string template)
    {
        var email = _renderer.Render(template, AllValues);

        email.Subject.ShouldNotBeNullOrWhiteSpace();
        email.Subject.ShouldNotContain("{{");
        email.Html.ShouldContain("lang=\"pt-BR\"");
        email.Html.ShouldContain("https://portal.test/x?token=abc");
        email.Text.ShouldContain("https://portal.test/x?token=abc");
        email.Text.ShouldNotStartWith("Subject:");
        email.Html.ShouldNotContain("{{");
        email.Text.ShouldNotContain("{{");
    }

    public static TheoryData<string> TemplateNames() => [.. Templates];

    [Fact]
    public void Html_EncodesEveryPlaceholder_ButTheTextKeepsTheRawValue()
    {
        var values = new Dictionary<string, string>(AllValues) { ["company"] = "<script>alert('x')</script>" };

        var email = _renderer.Render("trial-ending", values);

        email.Html.ShouldNotContain("<script>");
        email.Html.ShouldContain("&lt;script&gt;");
        email.Text.ShouldContain("<script>alert('x')</script>");
    }

    [Fact]
    public void MissingPlaceholderValue_Throws() =>
        Should.Throw<InvalidOperationException>(() => _renderer.Render("trial-ending", new Dictionary<string, string> { ["company"] = "Acme" }));

    [Theory]
    [InlineData(123_456, "R$ 1.234,56")]
    [InlineData(4_900, "R$ 49,00")]
    [InlineData(5, "R$ 0,05")]
    [InlineData(100_000_000, "R$ 1.000.000,00")]
    public void Money_IsBrazilianReal(long cents, string expected) => PtBr.Money(cents).ShouldBe(expected);

    [Fact]
    public void Dates_AreShownInSaoPauloTime()
    {
        var utc = new DateTimeOffset(2026, 2, 1, 2, 30, 0, TimeSpan.Zero);

        PtBr.DateTime(utc).ShouldBe("31/01/2026 às 23:30");
        PtBr.Date(utc).ShouldBe("31/01/2026");
    }

    [Theory]
    [InlineData("card_declined", "cartão recusado")]
    [InlineData("insufficient_funds", "saldo insuficiente")]
    [InlineData("whatever", "motivo não informado pelo banco")]
    public void PaymentFailure_ReasonIsTranslated(string code, string expected) =>
        OnPaymentFailed.Reason(code).ShouldBe(expected);
}
