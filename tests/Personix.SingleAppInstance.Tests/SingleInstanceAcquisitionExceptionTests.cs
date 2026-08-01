using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

public class SingleInstanceAcquisitionExceptionTests
{
    [Fact]
    public void DefaultConstructor_CreatesInstanceWithoutInnerException()
    {
        var exception = new SingleInstanceAcquisitionException();

        exception.Message.ShouldNotBeNullOrWhiteSpace();
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void MessageConstructor_SetsMessageAndLeavesInnerExceptionNull()
    {
        var exception = new SingleInstanceAcquisitionException("boom");

        exception.Message.ShouldBe("boom");
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void MessageAndInnerExceptionConstructor_SetsBoth()
    {
        var inner = new UnauthorizedAccessException("inner boom");

        var exception = new SingleInstanceAcquisitionException("outer boom", inner);

        exception.Message.ShouldBe("outer boom");
        exception.InnerException.ShouldBeSameAs(inner);
    }

    [Fact]
    public void IsAnInvalidOperationException()
    {
        var exception = new SingleInstanceAcquisitionException("boom");

        exception.ShouldBeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void IsNotASingleInstanceException()
    {
        // Deliberately not related to SingleInstanceException by inheritance: that type specifically
        // means "another instance is confirmed to be running". Code that catches SingleInstanceException
        // to handle that case must not also silently catch this one, which means the opposite -- the
        // attempt failed before it could determine whether another instance is running at all.
        var exception = new SingleInstanceAcquisitionException("boom");

        exception.ShouldNotBeAssignableTo<SingleInstanceException>();
    }
}
