using System.Windows.Threading;

namespace Zhuomian.Ui.WpfCandidate;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--smoke") { CandidateSmoke.Run(Console.WriteLine, AppContext.BaseDirectory); return 0; }
            if (args.Length != 0) { Console.Error.WriteLine("unsupported_arguments"); return 2; }
            Console.WriteLine("E2-3 WPF engineering candidate. Synthetic only. Independent HWND; not accepted desktop integration. Exit with × or console close.");
            using var window = new CandidateWindow(Console.WriteLine);
            window.Start(); Dispatcher.Run(); return window.ExitCode;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { Console.Error.WriteLine("candidate_failed " + ex.GetType().Name); return 1; }
    }
}
