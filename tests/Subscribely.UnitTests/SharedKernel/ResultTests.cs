using Subscribely.SharedKernel;

namespace Subscribely.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_HasValueAndNoError()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_ExposesErrorAndThrowsOnValue()
    {
        Result<int> result = Error.Conflict("duplicate", "Already exists.");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("duplicate");
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Entity_RaisesAndClearsEvents()
    {
        var entity = new SampleEntity();

        entity.DoSomething();
        entity.Events.Count.ShouldBe(1);

        entity.ClearEvents();
        entity.Events.ShouldBeEmpty();
    }

    private sealed record SampleHappened : IIntegrationEvent;

    private sealed class SampleEntity : Entity
    {
        public void DoSomething() => Raise(new SampleHappened());
    }
}
