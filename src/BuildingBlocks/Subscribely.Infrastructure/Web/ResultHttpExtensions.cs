using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Subscribely.SharedKernel;

namespace Subscribely.Infrastructure.Web;

public static class ResultHttpExtensions
{
    public const string ErrorTypeBaseUri = "https://subscribely.dev/errors/";

    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.PaymentRequired => StatusCodes.Status402PaymentRequired,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Gone => StatusCodes.Status410Gone,
        ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Converte um erro de negócio em ProblemDetails (RFC 9457).</summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Details is not null)
        {
            foreach (var (key, value) in error.Details)
            {
                extensions[key] = value;
            }
        }

        return TypedResults.Problem(
            detail: error.Message,
            statusCode: error.Type.ToStatusCode(),
            title: error.Code,
            type: ErrorTypeBaseUri + error.Code,
            extensions: extensions);
    }
}
