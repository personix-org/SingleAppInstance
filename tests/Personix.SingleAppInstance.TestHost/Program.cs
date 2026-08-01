// Personix.SingleAppInstance.TestHost
//
// Minimal cross-process worker used ONLY by SingleInstanceGuardCrossProcessTests to prove real,
// OS-level single-instance exclusion. It is not shipped and not referenced by the package itself.
//
// Protocol (stdout is the sync channel, stdin is the control channel, both line-based):
//   args: hold <applicationId> [Global|PerUser]
//
//   Immediately after trying to acquire, the process writes exactly one line to stdout and flushes:
//     "ACQUIRED" or "BLOCKED"
//
//   If blocked, the process exits right away with code 1.
//
//   If acquired, the process then blocks reading a single line from stdin:
//     "EXIT"        -> disposes the guard (graceful release), prints "EXITED", exits with code 0.
//     anything else -> returns without ever disposing, i.e. an ungraceful shutdown.
//   Callers that want to simulate a real crash instead of an ungraceful-but-cooperative shutdown
//   should just Process.Kill() this process once "ACQUIRED" has been observed on stdout.

using Personix.SingleAppInstance;

if (args.Length < 2 || !string.Equals(args[0], "hold", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage: Personix.SingleAppInstance.TestHost hold <applicationId> [Global|PerUser]");
    return 2;
}

var applicationId = args[1];
var scope = args.Length > 2 && Enum.TryParse<SingleInstanceScope>(args[2], ignoreCase: true, out var parsedScope)
    ? parsedScope
    : SingleInstanceScope.Global;

var acquired = SingleInstanceGuard.TryAcquire(applicationId, out var guard, scope);

Console.WriteLine(acquired ? "ACQUIRED" : "BLOCKED");
Console.Out.Flush();

if (!acquired)
{
    return 1;
}

var command = Console.ReadLine();
if (string.Equals(command, "EXIT", StringComparison.Ordinal))
{
    guard!.Dispose();
    Console.WriteLine("EXITED");
    Console.Out.Flush();
}

return 0;
