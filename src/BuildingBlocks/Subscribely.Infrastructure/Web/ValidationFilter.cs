using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Subscribely.Infrastructure.Web;

/// <summary>
/// Valida o corpo da requisição com DataAnnotations e devolve 400 (ValidationProblem).
/// Usado no lugar do <c>AddValidation()</c> nativo porque os endpoints vivem em bibliotecas de módulo.
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "invalid-body",
                detail: "Request body is required.",
                type: ResultHttpExtensions.ErrorTypeBaseUri + "invalid-body");
        }

        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            return await next(context);
        }

        var errors = results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty]).Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => JsonCamelCase(x.Member))
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").ToArray());

        return TypedResults.ValidationProblem(errors, type: ResultHttpExtensions.ErrorTypeBaseUri + "validation");
    }

    private static string JsonCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder.AddEndpointFilter<ValidationFilter<TRequest>>().ProducesValidationProblem();
}
