namespace Personix.SingleAppInstance;

/// <summary>
/// Thrown when acquiring the single-instance lock fails for a reason unrelated to another instance
/// already holding it -- for example an <see cref="UnauthorizedAccessException"/> because a
/// <c>Global\</c>-scoped mutex already exists but was created under a different user account and this
/// process' security context is not allowed to open it.
/// </summary>
/// <remarks>
/// Deliberately does not derive from <see cref="SingleInstanceException"/>: that type specifically means
/// "another instance is confirmed to be running", which is not the case here -- the acquisition attempt
/// failed before it could even determine that. Code that catches <see cref="SingleInstanceException"/> to
/// handle "already running" will not also silently catch this one; a failure to even ask the operating
/// system whether the lock is held is an environment problem that deserves its own handling (or to
/// propagate as an unhandled exception), not to be reported as "someone else is running".
/// </remarks>
public class SingleInstanceAcquisitionException : InvalidOperationException
{
    /// <summary>Creates the exception without a message.</summary>
    public SingleInstanceAcquisitionException()
    {
    }

    /// <summary>Creates the exception with a message describing why the lock could not be acquired.</summary>
    /// <param name="message">The message that describes the error.</param>
    public SingleInstanceAcquisitionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">
    /// The exception that caused this one, or <see langword="null"/> if there isn't one.
    /// </param>
    public SingleInstanceAcquisitionException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
