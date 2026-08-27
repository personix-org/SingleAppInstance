using System.Reflection;
using Shouldly;
using Xunit;

namespace Personix.SingleAppInstance.Tests;

public class SingleInstanceGuardConcurrencyTests
{
    [Fact]
    public async Task TryAcquire_ManyConcurrentCallersForSameApplicationId_ExactlyOneSucceeds()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";
        const int attempts = 20;

        var tasks = Enumerable.Range(0, attempts)
            .Select(_ => Task.Run(() => SingleInstanceGuard.TryAcquire(appId, out var guard) ? guard : null))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var winners = results.Where(guard => guard is not null).ToArray();

        try
        {
            winners.Length.ShouldBe(1);
        }
        finally
        {
            foreach (var guard in winners)
            {
                guard!.Dispose();
            }
        }
    }

    [Fact]
    public void TryAcquire_AfterAFailedAttempt_SetsGuardToNull_AndDoesNotBlockALaterSuccessfulAttempt()
    {
        var appId = $"TestApp-{Guid.NewGuid():N}";

        SingleInstanceGuard.TryAcquire(appId, out var first).ShouldBeTrue();

        var secondAcquired = SingleInstanceGuard.TryAcquire(appId, out var second);
        secondAcquired.ShouldBeFalse();
        second.ShouldBeNull();

        first!.Dispose();

        var thirdAcquired = SingleInstanceGuard.TryAcquire(appId, out var third);
        thirdAcquired.ShouldBeTrue();
        third.ShouldNotBeNull();
        third!.Dispose();
    }

    [Fact]
    public void TryAcquire_RepeatedRealThreadRaces_AlwaysLeaveExactlyOneWinnerAndConsistentBookkeeping()
    {
        // Regression guard for the TOCTOU window that used to exist in TryAcquire: the "do we already
        // own this mutex" check and the bookkeeping write into `AcquiredMutexes` used to run in two
        // separate `lock` blocks with the real OS mutex acquire happening in between, so two threads of
        // this process could both pass the check before either had written to the dictionary. Note this
        // does not reproduce an incorrect *result* even on the pre-fix code: the OS mutex was always the
        // final arbiter of which caller actually got the lock (see
        // TryAcquire_ManyConcurrentCallersForSameApplicationId_ExactlyOneSucceeds above, which already
        // passed reliably with the window open), so a black-box test cannot fail specifically against the
        // old code without adding test-only timing seams to production code. What this pins down instead
        // is the invariant the fix establishes deliberately: after every racing attempt has settled,
        // exactly one guard was handed out and the in-process bookkeeping agrees with it -- run
        // repeatedly, with real OS threads (not the thread pool) released simultaneously, to make the
        // narrow window between a thread starting and it performing its check as easy as possible to hit
        // on any given iteration.
        var acquiredMutexesField = typeof(SingleInstanceGuard).GetField("AcquiredMutexes", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SingleInstanceGuard.AcquiredMutexes not found by reflection.");
        var buildMutexNameMethod = typeof(SingleInstanceGuard).GetMethod("BuildMutexName", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SingleInstanceGuard.BuildMutexName(string, SingleInstanceScope) not found by reflection.");
        var acquiredMutexes = (System.Collections.IDictionary)acquiredMutexesField.GetValue(null)!;

        const int rounds = 50;
        const int racersPerRound = 8;

        for (var round = 0; round < rounds; round++)
        {
            var appId = $"TestApp-{Guid.NewGuid():N}";
            var mutexName = (string)buildMutexNameMethod.Invoke(null, [appId, SingleInstanceScope.Global])!;

            using var ready = new CountdownEvent(racersPerRound);
            using var go = new ManualResetEventSlim(false);
            var results = new bool[racersPerRound];
            var guards = new SingleInstanceGuard?[racersPerRound];
            var threads = new Thread[racersPerRound];

            for (var i = 0; i < racersPerRound; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    ready.Signal();
                    go.Wait(TimeSpan.FromSeconds(10));
                    results[index] = SingleInstanceGuard.TryAcquire(appId, out guards[index]);
                })
                {
                    IsBackground = true,
                };
                threads[i].Start();
            }

            ready.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            go.Set();

            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            }

            try
            {
                results.Count(r => r).ShouldBe(1, $"round {round}: exactly one racer should have acquired the lock.");
                acquiredMutexes.Contains(mutexName).ShouldBeTrue(
                    $"round {round}: the winning racer's bookkeeping entry must survive -- a mishandled " +
                    "check-then-write race could roll it back incorrectly.");
            }
            finally
            {
                foreach (var guard in guards)
                {
                    guard?.Dispose();
                }
            }

            acquiredMutexes.Contains(mutexName).ShouldBeFalse(
                $"round {round}: disposing the winner must remove the bookkeeping entry so a later attempt is not blocked.");
        }
    }
}
