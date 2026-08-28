using System.Globalization;
using System.Text.Json;

namespace Zhuomian.Spike.PlatformLifecycle;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--fullscreen-child")
        {
            return RunFullscreenChild(args[1]);
        }

        var output = ArgumentValue(args, "--output") ?? "platform-lifecycle.json";
        try
        {
            var evidence = PlatformLifecycleProbe.Run();
            File.WriteAllText(
                output,
                JsonSerializer.Serialize(evidence, PlatformLifecycleJsonContext.Default.PlatformLifecycleEvidence));
            return evidence.Passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static int RunFullscreenChild(string readyPath)
    {
        using var form = new FullscreenExternalForm(Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty);
        form.Shown += (_, _) => File.WriteAllText(
            readyPath,
            form.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
        Application.Run(form);
        return 0;
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
