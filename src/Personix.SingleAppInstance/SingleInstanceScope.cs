namespace Personix.SingleAppInstance;

/// <summary>
/// Determines how widely the single-instance lock is shared on the machine.
/// </summary>
/// <remarks>
/// Meaningful on Windows only, where <see cref="Global"/> maps to the <c>Global\</c> mutex namespace
/// and <see cref="PerUser"/> to <c>Local\</c>. .NET has no equivalent namespace split on Linux or
/// macOS, so there the scope is ignored and every value produces the same mutex name.
/// </remarks>
public enum SingleInstanceScope
{
    /// <summary>
    /// The lock applies within the current user's Windows session: this user cannot run two instances
    /// at once, but each other session enforces its own lock independently. Ignored outside Windows.
    /// </summary>
    PerUser,

    /// <summary>
    /// The lock is shared by every session on the machine, so at most one instance runs across all
    /// users at once. The default used by every acquire method in this package. Ignored outside
    /// Windows.
    /// </summary>
    Global
}

