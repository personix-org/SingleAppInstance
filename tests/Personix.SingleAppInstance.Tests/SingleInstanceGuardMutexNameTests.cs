using System.Reflection;
using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

public class SingleInstanceGuardMutexNameTests
{
    [Fact]
    public void DifferentScopes_SameApplicationId_BehaveAccordingToPlatformContract()
    {
        // README.md: "Scope applies on Windows only ... a single name is used" elsewhere. This asserts
        // both halves of that contract for whichever platform the suite actually runs on.
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using var global = SingleInstanceGuard.ThrowIfAlreadyRunning(appId, SingleInstanceScope.Global);

        var acquiredPerUser = SingleInstanceGuard.TryAcquire(appId, out var perUserGuard, SingleInstanceScope.PerUser);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                acquiredPerUser.ShouldBeTrue("Global\\ and Local\\ are separate mutex namespaces on Windows.");
                perUserGuard.ShouldNotBeNull();
            }
            else
            {
                acquiredPerUser.ShouldBeFalse("Non-Windows platforms have no namespace to map scope to, so both scopes share one mutex name.");
                perUserGuard.ShouldBeNull();
            }
        }
        finally
        {
            perUserGuard?.Dispose();
        }
    }

    [Theory]
    [InlineData("App/With\\Illegal:Characters*?\"<>|")]
    [InlineData("Ünïcode-Ápp-Ñame-日本語")]
    public void ApplicationId_WithCharactersIllegalInRawMutexNames_IsSafeToUse(string appId)
    {
        // README.md: "ids containing path separators or other characters that are illegal in a mutex
        // name are safe to use" because the id is hashed before becoming part of the mutex name.
        using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning(appId);

        guard.ShouldNotBeNull();
    }

    [Fact]
    public void ApplicationId_VeryLong_IsSafeToUse()
    {
        var appId = new string('a', 5000);

        using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning(appId);

        guard.ShouldNotBeNull();
    }

    [Fact]
    public void GetDefaultApplicationId_ReturnsTheEntryAssemblyName()
    {
        // Reflection, not the public API: the parameterless overloads derive their id from
        // Assembly.GetEntryAssembly(), which under "dotnet test" resolves to the shared VSTest host
        // process ("testhost"), not to this test assembly. Exercising that through the public API would
        // make this test collide with any other, unrelated test project's own use of the parameterless
        // overload if it happened to run at the same moment on this machine. Reflection gives a
        // deterministic, side-effect-free check of the same logic.
        var method = typeof(SingleInstanceGuard).GetMethod("GetDefaultApplicationId", BindingFlags.NonPublic | BindingFlags.Static);

        method.ShouldNotBeNull("SingleInstanceGuard is expected to expose a private static GetDefaultApplicationId().");

        var actual = (string?)method!.Invoke(null, null);
        var expected = Assembly.GetEntryAssembly()?.GetName().Name;

        actual.ShouldNotBeNullOrWhiteSpace();
        actual.ShouldBe(expected);
    }

    [Fact]
    public void TryAcquire_Parameterless_ReturnsTrueAndUsableGuard()
    {
        var acquired = SingleInstanceGuard.TryAcquire(out var guard);

        try
        {
            acquired.ShouldBeTrue();
            guard.ShouldNotBeNull();
        }
        finally
        {
            guard?.Dispose();
        }
    }

    [Fact]
    public void ThrowIfAlreadyRunning_Parameterless_ReturnsUsableGuard()
    {
        using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning();

        guard.ShouldNotBeNull();
    }
}
