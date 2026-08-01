using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

public class SingleInstanceGuardTests
{
    [Fact]
    public void TryAcquire_WhenFirstInstance_ReturnsTrue()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        var acquired = SingleInstanceGuard.TryAcquire(appId, out var guard);

        acquired.ShouldBeTrue();
        guard.ShouldNotBeNull();
        guard!.Dispose();
    }

    [Fact]
    public async Task TryAcquire_WhenSecondInstance_ReturnsFalse()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        var firstAcquired = SingleInstanceGuard.TryAcquire(appId, out var guard);
        firstAcquired.ShouldBeTrue();
        guard.ShouldNotBeNull();

        var secondAcquired = await Task.Run(() => SingleInstanceGuard.TryAcquire(appId, out _));

        guard!.Dispose();
        secondAcquired.ShouldBeFalse();
    }

    [Fact]
    public void ThrowIfAlreadyRunning_WhenFirstInstance_ReturnsGuard()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        var guard = SingleInstanceGuard.ThrowIfAlreadyRunning(appId);

        guard.ShouldNotBeNull();
        guard.Dispose();
    }

    [Fact]
    public void ThrowIfAlreadyRunning_WhenSecondInstance_ThrowsException()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning(appId);

        var exception = Should.Throw<SingleInstanceException>(() =>
            SingleInstanceGuard.ThrowIfAlreadyRunning(appId));

        exception.Message.ShouldContain(appId);
    }
}

