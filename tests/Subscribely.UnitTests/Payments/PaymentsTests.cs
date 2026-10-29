using Subscribely.Modules.Payments.Contracts;
using Subscribely.Modules.Payments.Domain;
using Subscribely.Modules.Payments.Features.Webhooks;
using Subscribely.Modules.Payments.Gateway;

namespace Subscribely.UnitTests.Payments;

public sealed class WebhookSignatureTests
{
    private const string Secret = "whsec_test";
    private const string Body = """{"id":"evt_1","type":"charge.succeeded"}""";
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_SignatureWithinTolerance_IsAccepted()
    {
        var header = WebhookSignature.Sign(Secret, Body, Now);

        header.ShouldStartWith($"t={Now.ToUnixTimeSeconds()},v1=");
        WebhookSignature.Verify(Secret, header, Body, Now.AddMinutes(4)).ShouldBeTrue();
    }

    [Fact]
    public void Expired_SignatureOlderThan5Minutes_IsRejected() =>
        WebhookSignature.Verify(Secret, WebhookSignature.Sign(Secret, Body, Now), Body, Now.AddMinutes(5).AddSeconds(1)).ShouldBeFalse();

    [Fact]
    public void FutureTimestamp_BeyondTolerance_IsRejected() =>
        WebhookSignature.Verify(Secret, WebhookSignature.Sign(Secret, Body, Now.AddMinutes(10)), Body, Now).ShouldBeFalse();

    [Fact]
    public void TamperedBody_IsRejected() =>
        WebhookSignature.Verify(Secret, WebhookSignature.Sign(Secret, Body, Now), Body.Replace("succeeded", "failed", StringComparison.Ordinal), Now).ShouldBeFalse();

    [Fact]
    public void OtherSecret_IsRejected() =>
        WebhookSignature.Verify("other", WebhookSignature.Sign(Secret, Body, Now), Body, Now).ShouldBeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=abc,v1=00")]
    [InlineData("t=1772366400")]
    [InlineData("t=1772366400,v1=not-hex")]
    [InlineData("t=99999999999999999999,v1=00")]
    public void Malformed_Header_IsRejected(string? header) =>
        WebhookSignature.Verify(Secret, header, Body, Now).ShouldBeFalse();

    [Fact]
    public void EmptySecret_NeverVerifies() =>
        WebhookSignature.Verify(string.Empty, WebhookSignature.Sign(string.Empty, Body, Now), Body, Now).ShouldBeFalse();
}

public sealed class DunningTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 7)]
    public void Schedule_IsDPlus1_3_7_FromIssue(int failures, int days) =>
        Dunning.NextAttemptFor(IssuedAt, failures).ShouldBe(IssuedAt.AddDays(days));

    [Fact]
    public void Schedule_EndsAfterTheThirdRetry() =>
        Dunning.NextAttemptFor(IssuedAt, 4).ShouldBeNull();

    [Fact]
    public void ScheduledFailures_PublishFailedThenExhausted()
    {
        var dunning = Dunning.Open(Guid.NewGuid(), Guid.NewGuid(), IssuedAt);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            dunning.RegisterScheduledFailure(attempt, "card_declined", IssuedAt.AddDays(attempt));
        }

        dunning.Events.OfType<PaymentFailed>().Select(e => e.NextAttemptAt).ShouldBe([IssuedAt.AddDays(1), IssuedAt.AddDays(3), IssuedAt.AddDays(7)]);
        dunning.Events.OfType<PaymentRetriesExhausted>().ShouldHaveSingleItem();
        dunning.IsOpen.ShouldBeFalse();
        dunning.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void ManualFailure_DoesNotConsumeTheSchedule()
    {
        var dunning = Dunning.Open(Guid.NewGuid(), Guid.NewGuid(), IssuedAt);
        dunning.RegisterScheduledFailure(1, "card_declined", IssuedAt);
        dunning.Pause();

        dunning.RegisterManualFailure(2, "card_declined", IssuedAt.AddHours(5));

        dunning.Attempts.ShouldBe(1);
        dunning.NextAttemptAt.ShouldBe(IssuedAt.AddDays(1));
    }

    [Fact]
    public void Resolved_IgnoresLateFailures()
    {
        var dunning = Dunning.Open(Guid.NewGuid(), Guid.NewGuid(), IssuedAt);
        dunning.Resolve(IssuedAt);

        dunning.RegisterScheduledFailure(1, "card_declined", IssuedAt);

        dunning.Events.ShouldBeEmpty();
    }
}

public sealed class FakeGatewayTests
{
    [Theory]
    [InlineData("pm_card_ok", 1, true, null)]
    [InlineData("pm_card_declined", 1, false, "card_declined")]
    [InlineData("pm_card_insufficient_funds", 1, false, "insufficient_funds")]
    [InlineData("pm_card_fail_then_ok", 1, false, "card_declined")]
    [InlineData("pm_card_fail_then_ok", 2, true, null)]
    [InlineData("pm_unknown", 1, false, "invalid_payment_method")]
    public void Decide_FollowsTheTestTokens(string token, int attempt, bool success, string? failureCode) =>
        FakeGatewayEndpoints.Decide(token, attempt).ShouldBe((success, failureCode));

    [Fact]
    public void ChargeKey_IsInvoiceAndAttempt()
    {
        var invoiceId = Guid.NewGuid();

        Charge.KeyFor(invoiceId, 2).ShouldBe($"{invoiceId}:2");
    }
}
