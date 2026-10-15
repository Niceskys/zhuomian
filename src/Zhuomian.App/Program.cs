using System.Reflection;
using Zhuomian.Platform.Windows;

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess)
{
    Console.Error.WriteLine("unsupported_environment: Windows 11 x64 required.");
    return 2;
}
Console.WriteLine("Zhuomian E2-I1 ENGINEERING fixture. One monitor only. May cover desktop icons.");
Console.WriteLine("build=" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
Console.WriteLine("No real launch, storage, search or animation. Use Space Exit, or close this console.");
try
{
    if (args.Length == 1 && args[0] == "--native-smoke")
    {
        NativeSmoke.Run(Console.WriteLine);
        return 0;
    }
    if (args.Length != 0) { Console.Error.WriteLine("unsupported_argument"); return 2; }
    using var host = new FallbackDesktopHost(code => Console.WriteLine($"{DateTime.UtcNow:O} {code}"));
    host.Start();
    return host.Run();
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    // Do not export exception paths, native payloads, command lines or user data.
    Console.Error.WriteLine("startup_or_runtime_failed: " + ex.GetType().Name);
    return 1;
}
