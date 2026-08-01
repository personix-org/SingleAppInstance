namespace Personix.SingleAppInstance;

/// <summary>
/// Thrown by <c>SingleInstanceGuard.ThrowIfAlreadyRunning</c> when another instance of the application
/// already holds the single-instance lock.
/// </summary>
public class SingleInstanceException : InvalidOperationException
{
    /// <summary>Creates the exception without a message.</summary>
    public SingleInstanceException()
    {
    }

    /// <summary>Creates the exception with a message describing why the lock could not be acquired.</summary>
    /// <param name="message">The message that describes the error.</param>
    public SingleInstanceException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">
    /// The exception that caused this one, or <see langword="null"/> if there isn't one.
    /// </param>
    public SingleInstanceException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

