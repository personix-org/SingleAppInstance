using System.Reflection;
using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

/// <summary>
/// Pins down what happens when creating or opening the underlying named mutex itself fails -- for a
/// reason that has nothing to do with another instance holding the lock, e.g.
/// <see cref="UnauthorizedAccessException"/> when a <c>Global\</c> mutex already exists but was created
/// under a different user account and this process' security context cannot open it.
/// </summary>
/// <remarks>
/// <para>
/// The public <see cref="SingleInstanceGuard.TryAcquire(string, out SingleInstanceGuard?, SingleInstanceScope)"/>
/// surface can never reach this failure naturally in this suite: the application id is always hashed down
/// to a fixed-length, always-legal raw mutex name before it reaches the operating system (see
/// <see cref="SingleInstanceGuardMutexNameTests.ApplicationId_VeryLong_IsSafeToUse"/> and
/// <see cref="SingleInstanceGuardMutexNameTests.ApplicationId_WithCharactersIllegalInRawMutexNames_IsSafeToUse"/>),
/// which is precisely what protects real callers from ever hitting this. And provoking the specific
/// <see cref="UnauthorizedAccessException"/> the code review called out requires a genuine ACL mismatch
/// between two different OS user accounts on the same machine, which this single-user sandbox cannot set
/// up deterministically.
/// </para>
/// <para>
/// What CAN be verified here, and is: that when the private construction step this package's own
/// production code goes through (<c>new Mutex(true, mutexName, out createdNew)</c>, inside
/// <c>SingleInstanceGuard.MutexOwnerThread.Run</c>) throws for ANY reason, the failure surfaces as a
/// distinct, truthful <see cref="SingleInstanceAcquisitionException"/> wrapping the original exception --
/// instead of being swallowed into a false "not acquired" the way it used to be. The fix does not
/// special-case by exception type (see the comment on the `catch` in <c>MutexOwnerThread.Run</c>), so
/// proving it for one real, OS-thrown exception type proves the mechanism for all of them, including
/// <see cref="UnauthorizedAccessException"/>. Reaching <c>MutexOwnerThread.Start</c> directly via
/// reflection is what makes this reachable at all: it is the one seam in the production code where a raw,
/// deliberately-illegal name can be supplied instead of one already sanitised by hashing.
/// </para>
/// </remarks>
public class SingleInstanceGuardMutexAcquisitionFailureTests
{
    [Fact]
    public void MutexOwnerThreadStart_WhenMutexConstructionThrows_ThrowsAcquisitionExceptionWithTheOriginalAsInnerException()
    {
        // .NET's own Mutex constructor documents and enforces a maximum name length; a raw name far
        // beyond it reliably throws ArgumentException from `new Mutex(...)` on this runtime (verified
        // empirically on macOS/arm64, .NET 10 -- the check is in shared, non-Windows-specific runtime
        // code, not an OS-specific code path).
        var tooLongMutexName = "SingleAppInstance-" + new string('a', 5000);

        var startMethod = GetMutexOwnerThreadStartMethod();
        var parameters = new object?[] { tooLongMutexName, null };

        var invocationException = Should.Throw<TargetInvocationException>(() => startMethod.Invoke(null, parameters));

        var thrown = invocationException.InnerException.ShouldBeOfType<SingleInstanceAcquisitionException>();
        thrown.InnerException.ShouldNotBeNull("the original exception from `new Mutex(...)` must be preserved, not discarded.");
        thrown.ShouldNotBeAssignableTo<SingleInstanceException>(
            "an acquisition failure must not be reported as the type that specifically means " +
            "\"another instance is confirmed to be running\".");

        // The old bug's exact shape: TryAcquire swallowed this and reported it exactly like a genuinely
        // occupied lock. Guard against the message regressing to that same unqualified claim.
        var looksLikeTheOldLie = System.Text.RegularExpressions.Regex.IsMatch(
            thrown.Message,
            @"^Another instance of '.*' is already running\.$");
        looksLikeTheOldLie.ShouldBeFalse("the message must not read like the genuine already-running message.");
        thrown.Message.ToLowerInvariant().ShouldContain("mutex");
    }

    [Fact]
    public void ThrowIfAlreadyRunning_MessageForAGenuinelyOccupiedLock_StillReportsAlreadyRunning()
    {
        // Contrast case: this suite's other findings must not make the *legitimate* "already running"
        // path start looking like an acquisition failure. When the lock really is held by another
        // instance -- the common, non-exceptional case -- the message must still say so.
        var appId = $"TestApp-{Guid.NewGuid():N}";
        using var first = SingleInstanceGuard.ThrowIfAlreadyRunning(appId);

        var exception = Should.Throw<SingleInstanceException>(() => SingleInstanceGuard.ThrowIfAlreadyRunning(appId));

        exception.Message.ShouldContain("already running");
        exception.InnerException.ShouldBeNull();
    }

    private static MethodInfo GetMutexOwnerThreadStartMethod()
    {
        var ownerThreadType = typeof(SingleInstanceGuard).GetNestedType("MutexOwnerThread", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SingleInstanceGuard.MutexOwnerThread not found by reflection.");

        // Start(...) is declared `public` on the (non-public) nested type itself, so its own binding
        // flag is Public even though the enclosing MutexOwnerThread type requires NonPublic to find.
        return ownerThreadType.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("MutexOwnerThread.Start(string, out bool) not found by reflection.");
    }
}
