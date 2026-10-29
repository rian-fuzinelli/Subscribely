using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Subscribely.Infrastructure.Security;
using Subscribely.Infrastructure.Tenancy;
using Subscribely.Infrastructure.Web;
using Subscribely.Modules.Billing.Domain.Invoices;
using Subscribely.Modules.Billing.Persistence;
using Subscribely.SharedKernel;

namespace Subscribely.Modules.Billing.Features.Invoices;

internal sealed class VoidInvoiceRequest
{
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Reason { get; init; } = string.Empty;
}

internal sealed record InvoiceSummaryResponse(
    Guid Id,
    string Number,
    string Reason,
    string Status,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalCents,
    string Currency,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt);

internal sealed record InvoiceLineResponse(string Description, long AmountCents, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd);

internal sealed record InvoiceResponse(
    Guid Id,
    string Number,
    string Reason,
    string Status,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    long TotalCents,
    string Currency,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    IReadOnlyList<InvoiceLineResponse> Lines)
{
    public static InvoiceResponse From(Invoice i) => new(
        i.Id, i.Number, i.Reason.ToString(), i.Status.ToString(), i.PeriodStart, i.PeriodEnd, i.TotalCents, Invoice.Currency,
        i.IssuedAt, i.PaidAt, i.VoidedAt, i.VoidReason,
        i.Lines.OrderBy(l => l.Position).Select(l => new InvoiceLineResponse(l.Description, l.AmountCents, l.PeriodStart, l.PeriodEnd)).ToList());
}

internal sealed record InvoicePageResponse(IReadOnlyList<InvoiceSummaryResponse> Items, string? NextCursor);

internal static class InvoiceEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/invoices").WithTags("Invoices").RequireTenantAdmin();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);

        endpoints.MapPost("/admin/invoices/{id:guid}/void", VoidAsync)
            .WithTags("Invoices (admin)").RequirePlatformAdmin().WithRequestValidation<VoidInvoiceRequest>();
        return endpoints;
    }

    /// <summary>Mais recentes primeiro; o cursor é a última posição <c>(issued_at, number)</c> da página anterior.</summary>
    private static async Task<IResult> ListAsync(string? cursor, int? limit, ITenantContext tenant, BillingDbContext db, CancellationToken ct)
    {
        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.RequiredTenantId);
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out var issuedAt, out var number))
            {
                return Error.Validation("cursor-invalid", "The cursor is invalid.").ToProblem();
            }

            query = query.Where(i => i.IssuedAt < issuedAt || (i.IssuedAt == issuedAt && string.Compare(i.Number, number) < 0));
        }

        var page = await query
            .OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Number)
            .Take(size + 1)
            .Select(i => new InvoiceSummaryResponse(
                i.Id, i.Number, i.Reason.ToString(), i.Status.ToString(), i.PeriodStart, i.PeriodEnd, i.TotalCents, Invoice.Currency, i.IssuedAt, i.PaidAt))
            .ToListAsync(ct);

        var items = page.Take(size).ToList();
        var next = page.Count > size ? Encode(items[^1].IssuedAt, items[^1].Number) : null;
        return TypedResults.Ok(new InvoicePageResponse(items, next));
    }

    private static async Task<IResult> GetAsync(Guid id, ITenantContext tenant, BillingDbContext db, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenant.RequiredTenantId, ct);
        return invoice is null ? NotFound().ToProblem() : TypedResults.Ok(InvoiceResponse.From(invoice));
    }

    private static async Task<IResult> VoidAsync(Guid id, VoidInvoiceRequest request, BillingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var invoice = await db.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return NotFound().ToProblem();
        }

        var voided = invoice.Void(request.Reason, time.GetUtcNow());
        if (!voided.IsSuccess)
        {
            return voided.Error.ToProblem();
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(InvoiceResponse.From(invoice));
    }

    private static Error NotFound() => Error.NotFound("invoice-not-found", "Invoice not found.");

    private static string Encode(DateTimeOffset issuedAt, string number) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{issuedAt.UtcTicks}|{number}")));

    private static bool TryDecode(string cursor, out DateTimeOffset issuedAt, out string number)
    {
        issuedAt = default;
        number = string.Empty;
        try
        {
            var parts = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)).Split('|', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            {
                return false;
            }

            issuedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            number = parts[1];
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
