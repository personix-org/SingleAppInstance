using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

public class SingleInstanceExceptionTests
{
    [Fact]
    public void DefaultConstructor_CreatesInstanceWithoutInnerException()
    {
        var exception = new SingleInstanceException();

        exception.Message.ShouldNotBeNullOrWhiteSpace();
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void MessageConstructor_SetsMessageAndLeavesInnerExceptionNull()
    {
        var exception = new SingleInstanceException("boom");

        exception.Message.ShouldBe("boom");
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public void MessageAndInnerExceptionConstructor_SetsBoth()
    {
        var inner = new InvalidOperationException("inner boom");

        var exception = new SingleInstanceException("outer boom", inner);

        exception.Message.ShouldBe("outer boom");
        exception.InnerException.ShouldBeSameAs(inner);
    }

    [Fact]
    public void IsAnInvalidOperationException()
    {
        var exception = new SingleInstanceException("boom");

        exception.ShouldBeAssignableTo<InvalidOperationException>();
    }
}
