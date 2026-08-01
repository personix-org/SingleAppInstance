using Personix.SingleAppInstance;

using var guard = SingleInstanceGuard.ThrowIfAlreadyRunning(SingleInstanceScope.Global);

Console.WriteLine("Single instance acquired. Press Enter to exit.");
Console.ReadLine();


