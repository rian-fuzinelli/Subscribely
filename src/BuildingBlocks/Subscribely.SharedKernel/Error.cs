namespace Subscribely.SharedKernel;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Unprocessable,
    Gone,
    PaymentRequired,
    TooManyRequests,
}

/// <summary>Erro de negócio esperado. <see cref="Code"/> é estável (kebab-case) e vira o "type" do ProblemDetails.</summary>
public sealed record Error(ErrorType Type, string Code, string Message)
{
    public IReadOnlyDictionary<string, object?>? Details { get; init; }

    public static Error Validation(string code, string message) => new(ErrorType.Validation, code, message);

    public static Error NotFound(string code = "not-found", string message = "Resource not found.") =>
        new(ErrorType.NotFound, code, message);

    public static Error Conflict(string code, string message) => new(ErrorType.Conflict, code, message);

    public static Error Unauthorized(string code = "unauthorized", string message = "Authentication failed.") =>
        new(ErrorType.Unauthorized, code, message);

    public static Error Forbidden(string code = "forbidden", string message = "Operation not allowed.") =>
        new(ErrorType.Forbidden, code, message);

    public static Error Unprocessable(string code, string message) => new(ErrorType.Unprocessable, code, message);

    public static Error Gone(string code, string message) => new(ErrorType.Gone, code, message);

    public static Error PaymentRequired(string code, string message) => new(ErrorType.PaymentRequired, code, message);

    public static Error TooManyRequests(string code, string message) => new(ErrorType.TooManyRequests, code, message);
}
