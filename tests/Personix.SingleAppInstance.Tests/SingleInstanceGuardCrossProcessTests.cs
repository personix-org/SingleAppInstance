using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

/// <summary>
/// Proves that SingleInstanceGuard actually excludes across OS process boundaries, which is the one
/// thing this package promises. Every other guard-vs-guard test in this suite runs inside a single test
/// process, where a second TryAcquire call for an application id already held is rejected by the
/// in-process "AcquiredMutexes" bookkeeping before the real named mutex is ever consulted -- so those
/// tests would still pass even if the underlying OS-level mutex logic were completely broken. These
/// tests launch Personix.SingleAppInstance.TestHost as genuinely separate processes, so only the real
/// mutex can make them pass.
/// </summary>
public class SingleInstanceGuardCrossProcessTests
{
    [Fact]
    public void SecondProcess_WhileFirstHoldsTheLock_IsBlocked()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using var first = TestHostProcess.Start(appId);
        first.ReadStatusLine().ShouldBe("ACQUIRED");

        using var second = TestHostProcess.Start(appId);
        second.ReadStatusLine().ShouldBe("BLOCKED");
        second.WaitForExit();

        first.SendExit();
        first.WaitForExit();
    }

    [Fact]
    public void AfterFirstProcessExitsGracefully_NextProcessCanAcquire()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using (var first = TestHostProcess.Start(appId))
        {
            first.ReadStatusLine().ShouldBe("ACQUIRED");
            first.SendExit();
            first.WaitForExit();
        }

        using var second = TestHostProcess.Start(appId);
        second.ReadStatusLine().ShouldBe("ACQUIRED");
        second.SendExit();
        second.WaitForExit();
    }

    [Fact]
    public void AfterFirstProcessIsKilled_MutexIsAbandonedAndNextProcessCanStillAcquire()
    {
        // Documents the README promise: "Abandoned mutexes are treated as free." Process.Kill gives the
        // child no chance to run Dispose, so this is a genuine crash simulation, not a cooperative exit.
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using (var first = TestHostProcess.Start(appId))
        {
            first.ReadStatusLine().ShouldBe("ACQUIRED");
            first.Kill();
        }

        using var second = TestHostProcess.Start(appId);
        second.ReadStatusLine().ShouldBe("ACQUIRED");
        second.SendExit();
        second.WaitForExit();
    }

    [Fact]
    public void SecondProcess_WithPerUserScope_WhileFirstHoldsPerUserLock_IsBlocked()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        using var first = TestHostProcess.Start(appId, SingleInstanceScope.PerUser);
        first.ReadStatusLine().ShouldBe("ACQUIRED");

        using var second = TestHostProcess.Start(appId, SingleInstanceScope.PerUser);
        second.ReadStatusLine().ShouldBe("BLOCKED");
        second.WaitForExit();

        first.SendExit();
        first.WaitForExit();
    }
}
