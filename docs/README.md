# Personix.SingleAppInstance

Enforces that only one instance of an application runs at a time. Useful for tray apps, background
agents, scheduled jobs, and anything else where a second copy would fight the first over a file, a
port, or a database.

## Contents

- `SingleInstanceGuard` – acquires and holds the instance lock; releases it on dispose.
- `SingleInstanceScope` – `Global` (whole machine) or `PerUser`.
- `SingleInstanceException` – thrown by `ThrowIfAlreadyRunning` when another instance holds the lock.
- `SingleInstanceAcquisitionException` – thrown by both methods when the lock could not even be checked
  (e.g. insufficient permissions), as opposed to another instance genuinely holding it.

## Installation

```xml
<PackageReference Include="Personix.SingleAppInstance" Version="1.1.5" />
```

## Usage

### Fail fast when already running

```csharp
using Personix.SingleAppInstance;

using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning();

// main application logic
```

`SingleInstanceException` is thrown when another instance already holds the lock.

### Exit quietly instead of throwing

```csharp
using Personix.SingleAppInstance;

if (!SingleInstanceGuard.TryAcquire(out var guard))
{
    Console.Error.WriteLine("Another instance is already running.");
    return 1;
}

using (guard)
{
    // main application logic
}

return 0;
```

### Explicit application id

Without an argument the id comes from the entry assembly name. Pass one explicitly when several
executables must share a single lock, or when one executable needs separate locks per configuration:

```csharp
using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning("Personix.Agent");
```

## API

| Member | Description |
|---|---|
| `TryAcquire(out guard, scope)` | `true` when the lock was taken, `false` when another instance holds it. Id is taken from the entry assembly. |
| `TryAcquire(applicationId, out guard, scope)` | Same, with an explicit id. Throws `ArgumentException` when the id is empty or whitespace. |
| `ThrowIfAlreadyRunning(scope)` | Returns the guard or throws `SingleInstanceException`. |
| `ThrowIfAlreadyRunning(applicationId, scope)` | Same, with an explicit id. |
| `Dispose()` | Releases the mutex. Safe to call repeatedly. |

Both methods default to `SingleInstanceScope.Global`.

## Notes

- **Hold the guard for the lifetime of the application.** The lock lives as long as the guard is not
  disposed, which is why the examples use `using` at the top of `Main`.
- **Scope applies on Windows only.** `Global` maps to the `Global\` mutex prefix and `Local\` for
  `PerUser`. On Linux and macOS .NET has no equivalent namespace split, so the scope is ignored and a
  single name is used.
- **The id is hashed.** The mutex name is `SingleAppInstance-{SHA256(applicationId)}`, so ids
  containing path separators or other characters that are illegal in a mutex name are safe to use.
- **Abandoned mutexes are treated as free.** If the previous holder crashed without releasing, the
  next instance acquires the lock rather than failing.
- **A failed attempt to even check the lock is not the same as "already running".** If creating or
  opening the underlying mutex fails for an unrelated reason (for example, an existing `Global\` mutex
  created by a different user account that this process is not allowed to open), both methods throw
  `SingleInstanceAcquisitionException` with the original exception as `InnerException`, instead of
  reporting a false "already running".
- **Dispose works from any thread.** The operating system lets only the thread that acquired a mutex
  release it, so the guard keeps a dedicated thread of its own that both acquires and releases —
  `Dispose()` merely signals it. That matters because `using var guard = ...;` followed by an `await`
  usually resumes on a different thread, and an earlier version of this package silently failed to
  release the lock in exactly that case: the next instance of the application would then refuse to
  start, and nothing said why. The cost is one extra thread for as long as the lock is held.

## Licence

MIT — see [LICENSE](LICENSE).
