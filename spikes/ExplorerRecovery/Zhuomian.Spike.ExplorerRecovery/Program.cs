using System.Text.Json;

namespace Zhuomian.Spike.ExplorerRecovery;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();

        var outputPath = GetOption(args, "--output");
        var readyPath = GetOption(args, "--ready");
        if (string.IsNullOrWhiteSpace(readyPath))
        {
            return 2;
        }

        var evidence = ExplorerRecoveryProbe.Run(readyPath);
        var json = JsonSerializer.Serialize(
            evidence,
            ExplorerRecoveryJsonContext.Default.ExplorerRecoveryEvidence);
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
