using System.Text.Json;

namespace Zhuomian.Spike.FallbackVisualUsability;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var output = ArgumentValue(args, "--output") ?? "fallback-visual-usability.json";
        var screenshot = ArgumentValue(args, "--screenshot") ?? "fallback-visual-usability.png";

        try
        {
            using var probe = new VisualUsabilityProbe();
            var evidence = probe.Run(screenshot);
            File.WriteAllText(
                output,
                JsonSerializer.Serialize(evidence, VisualUsabilityJsonContext.Default.VisualUsabilityEvidence));
            return evidence.Passed ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
