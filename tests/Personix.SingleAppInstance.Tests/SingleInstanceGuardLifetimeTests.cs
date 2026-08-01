using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

public class SingleInstanceGuardLifetimeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryAcquire_RejectsBlankApplicationId(string applicationId)
    {
        var exception = Should.Throw<ArgumentException>(() =>
            SingleInstanceGuard.TryAcquire(applicationId, out _));

        exception.ParamName.ShouldBe("applicationId");
    }

    [Fact]
    public void TryAcquire_RejectsNullApplicationId()
    {
        Should.Throw<ArgumentException>(() => SingleInstanceGuard.TryAcquire(null!, out _));
    }

    [Fact]
    public async Task Dispose_ReleasesTheLockForTheNextInstance()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        SingleInstanceGuard.TryAcquire(appId, out var first).ShouldBeTrue();
        first!.Dispose();

        var reacquired = await Task.Run(() => SingleInstanceGuard.TryAcquire(appId, out var second));

        reacquired.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        SingleInstanceGuard.TryAcquire(appId, out var guard).ShouldBeTrue();

        guard!.Dispose();
        guard.Dispose();
    }

    [Fact]
    public void DifferentApplicationIds_DoNotBlockEachOther()
    {
        var firstId = $"TestApp-{Guid.NewGuid():N}";
        var secondId = $"TestApp-{Guid.NewGuid():N}";

        using var first = SingleInstanceGuard.ThrowIfAlreadyRunning(firstId);
        using var second = SingleInstanceGuard.ThrowIfAlreadyRunning(secondId);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
    }

    [Fact]
    public void ApplicationId_IsTrimmedBeforeHashing()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning(appId);

        Should.Throw<SingleInstanceException>(() =>
            SingleInstanceGuard.ThrowIfAlreadyRunning($"  {appId}  "));
    }
}
