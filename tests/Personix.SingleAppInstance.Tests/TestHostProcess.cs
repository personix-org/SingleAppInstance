using System.Diagnostics;

namespace Personix.SingleAppInstance.Tests;

/// <summary>
/// Launches Personix.SingleAppInstance.TestHost as a genuinely separate OS process, so tests can prove
/// single-instance exclusion actually holds across process boundaries and not just within one AppDomain.
/// See Program.cs in the TestHost project for the stdout/stdin protocol.
/// </summary>
internal sealed class TestHostProcess : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly List<string> _stderrLines = [];
    private bool _disposed;

    private TestHostProcess(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                _stderrLines.Add(e.Data);
            }
        };
        _process.BeginErrorReadLine();
    }

    public static TestHostProcess Start(string applicationId, SingleInstanceScope scope = SingleInstanceScope.Global)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(TestHostLocator.AssemblyPath);
        startInfo.ArgumentList.Add("hold");
        startInfo.ArgumentList.Add(applicationId);
        startInfo.ArgumentList.Add(scope.ToString());

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Personix.SingleAppInstance.TestHost process.");

        return new TestHostProcess(process);
    }

    /// <summary>Blocks until the process reports whether it acquired the lock ("ACQUIRED"/"BLOCKED").</summary>
    public string ReadStatusLine()
    {
        return ReadLineWithTimeout();
    }

    /// <summary>Tells a process that reported "ACQUIRED" to release the guard and exit cleanly.</summary>
    public void SendExit()
    {
        _process.StandardInput.WriteLine("EXIT");
        _process.StandardInput.Flush();
    }

    public void WaitForExit()
    {
        if (!_process.WaitForExit((int)DefaultTimeout.TotalMilliseconds))
        {
            throw new TimeoutException(
                $"TestHost process {_process.Id} did not exit within {DefaultTimeout}. Stderr: {string.Join(" | ", _stderrLines)}");
        }
    }

    /// <summary>Terminates the process without giving it a chance to release the guard, simulating a crash.</summary>
    public void Kill()
    {
        _process.Kill(entireProcessTree: true);
        WaitForExit();
    }

    private string ReadLineWithTimeout()
    {
        var readTask = _process.StandardOutput.ReadLineAsync();

        if (!readTask.Wait(DefaultTimeout))
        {
            throw new TimeoutException(
                $"TestHost process {_process.Id} did not produce a status line within {DefaultTimeout}. Stderr: {string.Join(" | ", _stderrLines)}");
        }

        return readTask.Result
            ?? throw new InvalidOperationException(
                $"TestHost process {_process.Id} closed stdout without producing a status line. Stderr: {string.Join(" | ", _stderrLines)}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill(); nothing left to clean up.
        }

        _process.Dispose();
        _disposed = true;
    }
}
