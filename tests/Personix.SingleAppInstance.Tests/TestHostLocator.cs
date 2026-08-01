namespace Personix.SingleAppInstance.Tests;

/// <summary>
/// Resolves the build output of the Personix.SingleAppInstance.TestHost helper project. It mirrors the
/// configuration and target framework of the currently running test assembly, so it works the same way
/// whether the suite is built as Debug or Release.
/// </summary>
internal static class TestHostLocator
{
    public static string AssemblyPath { get; } = Resolve();

    private static string Resolve()
    {
        var targetFrameworkDir = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDir = targetFrameworkDir.Parent
            ?? throw new InvalidOperationException($"Could not determine the build configuration from '{AppContext.BaseDirectory}'.");
        var testsDir = configurationDir.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException($"Could not locate the tests root from '{AppContext.BaseDirectory}'.");

        var candidate = Path.Combine(
            testsDir.FullName,
            "Personix.SingleAppInstance.TestHost",
            "bin",
            configurationDir.Name,
            targetFrameworkDir.Name,
            "Personix.SingleAppInstance.TestHost.dll");

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"TestHost assembly not found at '{candidate}'. Build Personix.SingleAppInstance.TestHost " +
                "(it is referenced with ReferenceOutputAssembly=\"false\" from the test project) before " +
                "running the cross-process tests.",
                candidate);
        }

        return candidate;
    }
}
