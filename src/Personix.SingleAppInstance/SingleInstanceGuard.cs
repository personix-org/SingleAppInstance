namespace Personix.SingleAppInstance;

/// <summary>
/// Holds the lock that marks this process as the sole running instance of the application. Obtain one
/// through <c>TryAcquire</c> or <c>ThrowIfAlreadyRunning</c>, keep it alive for as long as the
/// application runs -- typically via a <c>using</c> declaration at the top of <c>Main</c> -- and let
/// <see cref="Dispose"/> release the lock so a future launch can acquire it.
/// </summary>
/// <remarks>
/// <para>
/// The underlying mutex is acquired and released on a dedicated background thread that this instance
/// owns, never on whichever thread happens to call <see cref="Dispose"/>. The operating system only
/// lets the thread that acquired a named mutex release it, and .NET has no API to hand that ownership
/// to another thread. The documented usage pattern <c>using var guard = ...;</c> followed by an
/// <c>await</c> routinely resumes on a different thread-pool thread than the one that acquired the
/// guard -- an earlier version of this package released the mutex directly on whichever thread called
/// <see cref="Dispose"/>, so on every thread other than the original one it silently did nothing, and
/// the mutex stayed held for as long as the process lived. The next launch of the application was then
/// wrongly refused to start, with nothing to explain why. This class instead starts one dedicated
/// thread per acquired lock; <see cref="Dispose"/> only signals that thread to release and waits for it
/// to finish, so the actual release always happens on the one thread allowed to make it. The cost is
/// one extra OS thread for as long as the guard is held.
/// </para>
/// <para>
/// A mutex left behind by a process that crashed or was killed without disposing its guard is treated
/// as free: the next acquire attempt succeeds instead of reporting the application as already running.
/// </para>
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    private static readonly Dictionary<string, object> _acquiredMutexes = new();
    private static readonly object _lock = new();

    private readonly MutexOwnerThread _ownerThread;
    private readonly string _mutexName;
    private bool _disposed;

    private SingleInstanceGuard(MutexOwnerThread ownerThread, string mutexName)
    {
        _ownerThread = ownerThread;
        _mutexName = mutexName;
    }

    /// <summary>
    /// Attempts to acquire the single-instance lock using the entry assembly's name as the application
    /// id, without throwing when another instance already holds it.
    /// </summary>
    /// <param name="guard">
    /// The acquired guard when this returns <see langword="true"/>; <see langword="null"/> otherwise.
    /// Dispose it when the application shuts down.
    /// </param>
    /// <param name="scope">
    /// Whether the lock is machine-wide or per-user. Defaults to <see cref="SingleInstanceScope.Global"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if another instance
    /// already holds it -- including a second call from within this same process. A mutex abandoned by
    /// a crashed instance counts as free, so this returns <see langword="true"/> in that case rather
    /// than failing.
    /// </returns>
    /// <exception cref="SingleInstanceAcquisitionException">
    /// Acquiring the lock failed for a reason unrelated to another instance already holding it -- for
    /// example, insufficient permissions to open a mutex created by a different user account. The
    /// original exception is available as <see cref="Exception.InnerException"/>.
    /// </exception>
    public static bool TryAcquire(out SingleInstanceGuard? guard, SingleInstanceScope scope = SingleInstanceScope.Global)
    {
        var applicationId = GetDefaultApplicationId();
        return TryAcquire(applicationId, out guard, scope);
    }

    /// <summary>
    /// Attempts to acquire the single-instance lock for the given application id, without throwing when
    /// another instance already holds it.
    /// </summary>
    /// <param name="applicationId">
    /// Identifies the application. Never stored or transmitted as-is -- only its SHA-256 hash becomes
    /// part of the mutex name, so ids containing path separators or other characters that would be
    /// illegal in a mutex name are safe to use. Use the same id everywhere the lock should be shared,
    /// and a different id per configuration when one executable needs independent locks.
    /// </param>
    /// <param name="guard">
    /// The acquired guard when this returns <see langword="true"/>; <see langword="null"/> otherwise.
    /// Dispose it when the application shuts down.
    /// </param>
    /// <param name="scope">
    /// Whether the lock is machine-wide or per-user. Defaults to <see cref="SingleInstanceScope.Global"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the lock was acquired; <see langword="false"/> if another instance
    /// already holds it -- including a second call for the same id from within this same process. A
    /// mutex abandoned by a crashed instance counts as free, so this returns <see langword="true"/> in
    /// that case rather than failing.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="applicationId"/> is empty or made up only of whitespace.
    /// </exception>
    /// <exception cref="SingleInstanceAcquisitionException">
    /// Acquiring the lock failed for a reason unrelated to another instance already holding it -- for
    /// example, insufficient permissions to open a mutex created by a different user account. The
    /// original exception is available as <see cref="Exception.InnerException"/>.
    /// </exception>
    public static bool TryAcquire(string applicationId, out SingleInstanceGuard? guard, SingleInstanceScope scope = SingleInstanceScope.Global)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("Application id must be a non-empty string.", nameof(applicationId));
        }

        var mutexName = BuildMutexName(applicationId, scope);

        lock (_lock)
        {
            // Check whether we already own this mutex in this process, and reserve it for this call in
            // the very same lock as the check. This closes the race where two threads of this process
            // could both see "not owned yet" and both go on to redundantly probe the real OS mutex below:
            // whichever thread's TryAdd loses here is rejected immediately, in-process, without ever
            // starting a MutexOwnerThread. The reservation is provisional: it stays in place on success,
            // or is rolled back in the `finally` below on failure -- including when
            // MutexOwnerThread.Start throws -- so a later attempt for the same application id is never
            // permanently blocked by a reservation that did not pan out.
            if (!_acquiredMutexes.TryAdd(mutexName, new object()))
            {
                guard = null;
                return false;
            }
        }

        // Deliberately outside the lock: MutexOwnerThread.Start() hands off to a dedicated background
        // thread for the real OS-level mutex operation. Holding _lock across that would block every other
        // TryAcquire/Dispose call in the process -- including for unrelated application ids -- for as
        // long as this one takes, and risks a deadlock if that other call is what would release this one.
        MutexOwnerThread? ownerThread = null;
        var acquired = false;
        try
        {
            ownerThread = MutexOwnerThread.Start(mutexName, out acquired);
        }
        finally
        {
            if (!acquired)
            {
                lock (_lock)
                {
                    _acquiredMutexes.Remove(mutexName);
                }
            }
        }

        if (!acquired)
        {
            guard = null;
            return false;
        }

        guard = new SingleInstanceGuard(ownerThread!, mutexName);
        return true;
    }

    /// <summary>
    /// Acquires the single-instance lock using the entry assembly's name as the application id, or
    /// throws when another instance already holds it.
    /// </summary>
    /// <param name="scope">
    /// Whether the lock is machine-wide or per-user. Defaults to <see cref="SingleInstanceScope.Global"/>.
    /// </param>
    /// <returns>The acquired guard. Dispose it when the application shuts down.</returns>
    /// <exception cref="SingleInstanceException">Another instance of the application already holds the lock.</exception>
    /// <exception cref="SingleInstanceAcquisitionException">
    /// Acquiring the lock failed for a reason unrelated to another instance already holding it. See
    /// <see cref="TryAcquire(string, out SingleInstanceGuard?, SingleInstanceScope)"/>.
    /// </exception>
    public static SingleInstanceGuard ThrowIfAlreadyRunning(SingleInstanceScope scope = SingleInstanceScope.Global)
    {
        var applicationId = GetDefaultApplicationId();
        return ThrowIfAlreadyRunning(applicationId, scope);
    }

    /// <summary>
    /// Acquires the single-instance lock for the given application id, or throws when another instance
    /// already holds it.
    /// </summary>
    /// <param name="applicationId">
    /// Identifies the application. Never stored or transmitted as-is -- only its SHA-256 hash becomes
    /// part of the mutex name, so ids containing path separators or other characters that would be
    /// illegal in a mutex name are safe to use.
    /// </param>
    /// <param name="scope">
    /// Whether the lock is machine-wide or per-user. Defaults to <see cref="SingleInstanceScope.Global"/>.
    /// </param>
    /// <returns>The acquired guard. Dispose it when the application shuts down.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="applicationId"/> is empty or made up only of whitespace.
    /// </exception>
    /// <exception cref="SingleInstanceException">Another instance of the application already holds the lock.</exception>
    /// <exception cref="SingleInstanceAcquisitionException">
    /// Acquiring the lock failed for a reason unrelated to another instance already holding it. See
    /// <see cref="TryAcquire(string, out SingleInstanceGuard?, SingleInstanceScope)"/>.
    /// </exception>
    public static SingleInstanceGuard ThrowIfAlreadyRunning(string applicationId, SingleInstanceScope scope = SingleInstanceScope.Global)
    {
        if (!TryAcquire(applicationId, out var guard, scope))
        {
            throw new SingleInstanceException($"Another instance of '{applicationId}' is already running.");
        }

        return guard!;
    }

    /// <summary>
    /// Releases the single-instance lock so a future launch of the application can acquire it. Safe to
    /// call more than once -- calls after the first do nothing.
    /// </summary>
    /// <remarks>
    /// The actual mutex release runs on the dedicated background thread that acquired it, never on
    /// whichever thread calls this method: the operating system only allows the thread that acquired a
    /// named mutex to release it, so this signals that thread and waits for it to finish rather than
    /// releasing the mutex itself. This matters because <c>using var guard = ...;</c> followed by an
    /// <c>await</c> commonly resumes on a different thread-pool thread than the one that acquired the
    /// guard -- an earlier version of this package released the mutex directly on whatever thread
    /// called this method, silently doing nothing when that thread did not match, which left the mutex
    /// held and the next launch of the application wrongly refused to start. If releasing the
    /// underlying mutex itself fails, whatever it threw is rethrown here instead of being lost on the
    /// background thread.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The mutex was acquired on _ownerThread's dedicated OS thread, and only that exact thread may
        // release it -- see the remarks on MutexOwnerThread below. Routing every Dispose() call through
        // it, regardless of which thread calls Dispose(), is what makes this safe to call from a thread
        // other than the one that acquired the guard (e.g. after an `await` resumes on a different
        // thread-pool thread).
        _ownerThread.ReleaseAndJoin();

        lock (_lock)
        {
            _acquiredMutexes.Remove(_mutexName);
        }
    }

    private static string GetDefaultApplicationId()
    {
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        var name = entryAssembly?.GetName().Name;

        return string.IsNullOrWhiteSpace(name)
            ? AppDomain.CurrentDomain.FriendlyName
            : name;
    }

    private static string BuildMutexName(string applicationId, SingleInstanceScope scope)
    {
        var normalized = applicationId.Trim();
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hex = Convert.ToHexString(hash);
        var baseName = $"SingleAppInstance-{hex}";

        if (OperatingSystem.IsWindows())
        {
            var prefix = scope == SingleInstanceScope.Global ? @"Global\" : @"Local\";
            return prefix + baseName;
        }

        return baseName;
    }

    // A named Mutex is owned by the OS thread that acquired it: Mutex.ReleaseMutex() throws unless it is
    // called from that exact thread, and there is no API to hand ownership to a different one. That is
    // enforced by the operating system, not by .NET, so managed code cannot work around it directly.
    //
    // The bug this class fixes: the package's own documented usage pattern
    // ("using var guard = ...; await Something();") routinely resumes the continuation after `await` on
    // a different thread-pool thread than the one that called TryAcquire. The previous implementation
    // called ReleaseMutex() only when Dispose() happened to run on the same managed thread that acquired
    // the guard, to dodge the ApplicationException the OS throws otherwise -- but on the "different
    // thread" branch it did nothing else to release the lock either, so the mutex silently stayed held
    // for as long as the original thread was alive, and the next TryAcquire (same process or a relaunch)
    // was wrongly denied.
    //
    // The fix: pin the acquire and the release to one dedicated, non-pooled background thread that this
    // type owns for the lifetime of the lock. TryAcquire() and Dispose() can be called from any thread;
    // they only ever talk to that dedicated thread through wait handles, so the actual
    // Mutex.WaitOne()/ReleaseMutex() pair always runs on the one real OS thread that is allowed to make
    // it, no matter which application thread is driving the guard.
    //
    // Cost of this approach: one extra native OS thread per acquire attempt (torn down immediately if
    // the attempt fails, kept alive only for as long as a successful guard is held). That is negligible
    // for how this package is used -- once per process startup, not in a hot loop.
    //
    // Alternatives considered and rejected:
    // - Named Semaphore instead of Mutex: semaphores have no thread affinity, but they also have no
    //   "abandoned" recovery. If the owning process crashes, the OS does not hand the slot back, which
    //   would break the documented "abandoned mutexes are treated as free" crash-recovery guarantee.
    // - A lock file (FileStream opened with FileShare.None): the OS releases the lock automatically when
    //   the process dies, same as an abandoned mutex, and file handles are not thread-affine. But it
    //   would replace the whole named-mutex/Global-Local-scope mechanism with a filesystem convention
    //   that has to pick a directory per scope, which is a much larger surface change than this bug fix
    //   calls for.
    private sealed class MutexOwnerThread
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _acquireCompleted = new(initialState: false);
        private readonly ManualResetEventSlim _releaseRequested = new(initialState: false);
        private readonly ManualResetEventSlim _releaseCompleted = new(initialState: false);
        private bool _acquired;
        private Exception? _acquireException;
        private Exception? _releaseException;

        private MutexOwnerThread(string mutexName)
        {
            _thread = new Thread(() => Run(mutexName))
            {
                IsBackground = true,
            };
        }

        public static MutexOwnerThread Start(string mutexName, out bool acquired)
        {
            var owner = new MutexOwnerThread(mutexName);
            owner._thread.Start();
            owner._acquireCompleted.Wait();

            acquired = owner._acquired;

            if (!acquired)
            {
                // Nothing was acquired, so there is nothing to release, and no guard will ever be handed
                // out for this attempt -- SingleInstanceGuard.Dispose() will never call ReleaseAndJoin()
                // on this instance. Tear it down here instead of leaking the thread and the wait handles.
                owner._thread.Join();
                var acquireException = owner._acquireException;
                owner.DisposeSignals();

                if (acquireException is not null)
                {
                    // Run() below could not even create or open the mutex, for a reason unrelated to
                    // another instance holding it -- that case is signalled without an exception at all,
                    // see the comment on WaitOne(0, false) there. Propagate it here, on the thread that
                    // called TryAcquire/ThrowIfAlreadyRunning, instead of losing it on this dedicated
                    // background thread where nobody could observe it (and where letting it go unhandled
                    // would crash the whole process, since this thread is not covered by any try/catch of
                    // the caller's). Wrapping it keeps this distinguishable from SingleInstanceException,
                    // which specifically means "another instance is confirmed to be running" -- this means
                    // the opposite: the attempt failed before that could even be determined.
                    throw new SingleInstanceAcquisitionException(
                        $"Failed to create or open the mutex for the single-instance lock ('{mutexName}'). " +
                        "This does not mean another instance is already running -- see the inner exception " +
                        "for the actual cause.",
                        acquireException);
                }
            }

            return owner;
        }

        public void ReleaseAndJoin()
        {
            _releaseRequested.Set();
            _releaseCompleted.Wait();
            _thread.Join();
            DisposeSignals();

            if (_releaseException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_releaseException).Throw();
            }
        }

        private void DisposeSignals()
        {
            _acquireCompleted.Dispose();
            _releaseRequested.Dispose();
            _releaseCompleted.Dispose();
        }

        private void Run(string mutexName)
        {
            Mutex? mutex = null;
            var ownsMutex = false;

            try
            {
                mutex = new Mutex(true, mutexName, out var createdNew);

                if (createdNew)
                {
                    // We created the mutex, so we own it
                    ownsMutex = true;
                }
                else
                {
                    // Mutex already exists, try to acquire it with zero timeout. A `false` result here
                    // means another instance genuinely holds the lock -- WaitOne(0, ...) never throws for
                    // that, it just returns false, so the catch below is never reached for that reason.
                    try
                    {
                        ownsMutex = mutex.WaitOne(0, false);
                    }
                    catch (AbandonedMutexException)
                    {
                        // Treat abandoned mutex as acquired so a new instance can continue
                        ownsMutex = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Creating or opening the mutex itself failed -- e.g. UnauthorizedAccessException when a
                // Global\ mutex already exists but was created under a different user account and this
                // process' security context cannot open it. This is NOT "another instance holds the
                // lock": as noted above, that case never throws here, it is signalled by WaitOne(0, ...)
                // returning false. Every exception this constructor can throw means the same thing --
                // acquisition could not even be attempted -- so there is no type here that would mean
                // "occupied" and need to be told apart from a real environment failure; catching broadly
                // and treating all of them as "environment failure" is the correct, not merely
                // convenient, choice. Swallowing this and falling through to "not acquired" would make
                // TryAcquire report `false` for a reason that has nothing to do with another instance
                // running -- and ThrowIfAlreadyRunning would then lie and say so in its exception message.
                // Capture it instead and hand it to Start(), which rethrows it on the thread that asked
                // for the lock (see the comment there for why it must not be thrown from this thread).
                _acquireException = ex;
                mutex?.Dispose();
                mutex = null;
            }

            _acquired = ownsMutex;

            if (!ownsMutex)
            {
                mutex?.Dispose();
                _acquireCompleted.Set();
                return;
            }

            _acquireCompleted.Set();

            // Block here -- on the same OS thread that just acquired the mutex -- until Dispose() asks
            // for it back. This is the entire point of the dedicated thread: ReleaseMutex() below always
            // runs on the thread that acquired it, no matter which application thread called Dispose().
            _releaseRequested.Wait();

            try
            {
                mutex!.ReleaseMutex();
            }
            catch (Exception ex)
            {
                // Surface this to whoever called Dispose() instead of crashing the process on this
                // background thread, which is what an unhandled exception here would otherwise do.
                _releaseException = ex;
            }
            finally
            {
                mutex!.Dispose();
            }

            _releaseCompleted.Set();
        }
    }
}
