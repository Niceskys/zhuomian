using System.Text.Json;

namespace Zhuomian.Spike.DesktopHosting;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outputPath = GetOption(args, "--output");

        using var probe = new FallbackHostProbe();
        var evidence = probe.Run();
        var json = JsonSerializer.Serialize(
            evidence,
            DesktopHostingJsonContext.Default.HostProbeEvidence);

        Console.WriteLine(json);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, json + Environment.NewLine);
        }

        return evidence.Passed ? 0 : 1;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
