using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Subscribely.Infrastructure.Web;
using Subscribely.SharedKernel;

namespace Subscribely.UnitTests.Infrastructure;

public sealed class ValidationFilterTests
{
    private static readonly EndpointFilterDelegate Next = _ => ValueTask.FromResult<object?>("ok");

    [Fact]
    public async Task ValidRequest_CallsNext()
    {
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), new SampleRequest { Name = "acme", Seats = 3 });

        var result = await new ValidationFilter<SampleRequest>().InvokeAsync(context, Next);

        result.ShouldBe("ok");
    }

    [Fact]
    public async Task InvalidRequest_ReturnsValidationProblemWithCamelCaseFields()
    {
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), new SampleRequest { Name = "", Seats = 0 });

        var result = await new ValidationFilter<SampleRequest>().InvokeAsync(context, Next);

        var problem = result.ShouldBeOfType<ValidationProblem>();
        problem.ProblemDetails.Errors.Keys.ShouldBe(["name", "seats"], ignoreOrder: true);
    }

    [Fact]
    public async Task MissingBody_ReturnsBadRequest()
    {
        var context = EndpointFilterInvocationContext.Create<SampleRequest?>(new DefaultHttpContext(), null);

        var result = await new ValidationFilter<SampleRequest>().InvokeAsync(context, Next);

        result.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.PaymentRequired, 402)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Gone, 410)]
    [InlineData(ErrorType.Unprocessable, 422)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    public void ErrorType_MapsToStatusCode(ErrorType type, int status) =>
        type.ToStatusCode().ShouldBe(status);

    [Fact]
    public void ToProblem_IncludesCodeAndDetails()
    {
        var error = Error.Conflict("seat-limit-reached", "No seats left.") with
        {
            Details = new Dictionary<string, object?> { ["maxSeats"] = 5 },
        };

        var problem = error.ToProblem().ProblemDetails;

        problem.Status.ShouldBe(409);
        problem.Type.ShouldBe("https://subscribely.dev/errors/seat-limit-reached");
        problem.Extensions["code"].ShouldBe("seat-limit-reached");
        problem.Extensions["maxSeats"].ShouldBe(5);
    }

    public sealed class SampleRequest
    {
        [Required]
        [MinLength(1)]
        public string Name { get; init; } = string.Empty;

        [Range(1, 100)]
        public int Seats { get; init; }
    }
}
