using System.Threading;
using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

/// <summary>
/// SingleInstanceGuard.Dispose() only calls Mutex.ReleaseMutex() when the calling thread matches the
/// thread that originally acquired the guard (see the `_ownerThreadId` check in Dispose()). These tests
/// pin down exactly what happens on the two sides of that check, using real, deliberately kept-alive
/// System.Threading.Thread instances so the outcome does not depend on thread-pool scheduling.
/// </summary>
public class SingleInstanceGuardDisposeThreadAffinityTests
{
    [Fact]
    public void Dispose_FromDifferentThread_WhileOwningThreadIsStillAlive_ReleasesTheLockForTheNextAcquire()
    {
        // KNOWN BUG -- reported to @ag-developer, not fixed here (tester, not developer).
        //
        // Dispose() skips ReleaseMutex() when called off the owning thread, to avoid the
        // ApplicationException the OS would throw for releasing a mutex you don't own on this thread.
        // But it then does nothing else to release the lock either: Mutex.Dispose() just closes this
        // process's handle, it does not call ReleaseMutex. As long as the original owning thread is
        // still alive, the OS mutex stays locked -- Dispose() is a silent no-op, with no exception and
        // no other signal that the lock was not released.
        //
        // This is not an exotic corner case. It is the default outcome of the package's own documented
        // usage pattern ("using var guard = ...; // main application logic") the moment that logic
        // contains an `await`: a continuation can resume on a different thread-pool thread than the one
        // that called TryAcquire/ThrowIfAlreadyRunning. The application believes the lock was released;
        // a relaunch (or even a same-process retry, as asserted below) is wrongly denied until the
        // original thread happens to terminate on its own.
        var appId = $"TestApp-{Guid.NewGuid():N}";
        SingleInstanceGuard? guard = null;
        using var acquiredSignal = new ManualResetEventSlim(false);
        using var releaseOwnerThreadSignal = new ManualResetEventSlim(false);

        var ownerThread = new Thread(() =>
        {
            SingleInstanceGuard.TryAcquire(appId, out guard).ShouldBeTrue();
            acquiredSignal.Set();
            releaseOwnerThreadSignal.Wait(TimeSpan.FromSeconds(30));
        })
        {
            IsBackground = true,
        };
        ownerThread.Start();

        try
        {
            acquiredSignal.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            guard.ShouldNotBeNull();

            // Dispose from THIS (test) thread -- deliberately not ownerThread, which is still alive and
            // blocked on releaseOwnerThreadSignal at this point.
            Should.NotThrow(() => guard!.Dispose());

            var reacquired = SingleInstanceGuard.TryAcquire(appId, out var second);
            try
            {
                reacquired.ShouldBeTrue(
                    "Dispose() from a non-owning thread must still release the lock; otherwise the guard " +
                    "silently leaks the mutex for as long as the original owning thread stays alive.");
            }
            finally
            {
                second?.Dispose();
            }
        }
        finally
        {
            releaseOwnerThreadSignal.Set();
            ownerThread.Join(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void Dispose_FromDifferentThread_AfterOwningThreadHasTerminated_ReleasesTheLock()
    {
        // Contrast case for the bug above: once the original owning thread has actually terminated
        // (not merely "some other thread called Dispose"), the OS/runtime's abandoned-mutex recovery
        // kicks in and a same-process reacquire succeeds, even though nobody ever called Dispose() from
        // the owning thread itself. This is the same recovery mechanism documented for process crashes,
        // just triggered by thread death instead of process death.
        var appId = $"TestApp-{Guid.NewGuid():N}";
        SingleInstanceGuard? guard = null;
        using var acquiredSignal = new ManualResetEventSlim(false);

        var ownerThread = new Thread(() =>
        {
            SingleInstanceGuard.TryAcquire(appId, out guard).ShouldBeTrue();
            acquiredSignal.Set();
            // Returns immediately: the thread terminates without ever disposing or releasing.
        })
        {
            IsBackground = true,
        };
        ownerThread.Start();

        acquiredSignal.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        guard.ShouldNotBeNull();
        ownerThread.Join(TimeSpan.FromSeconds(10)); // The owning thread is now truly gone.

        Should.NotThrow(() => guard!.Dispose());

        var reacquired = SingleInstanceGuard.TryAcquire(appId, out var second);
        try
        {
            reacquired.ShouldBeTrue();
        }
        finally
        {
            second?.Dispose();
        }
    }
}
