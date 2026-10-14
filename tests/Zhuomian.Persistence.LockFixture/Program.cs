// Repository-owned no-resource fixture. Parent supplies only its isolated temporary test root.
if (args.Length is not (1 or 2)) { return 2; }
string root = Path.GetFullPath(args[0]);
if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal) ||
    !Path.GetFileName(root).StartsWith("zhuomian-persistence-", StringComparison.Ordinal) || !Directory.Exists(root)) { return 3; }
using var mutex = new FileStream(Path.Combine(root, "workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
if (args.Length == 2)
{
    if (args[1] != "corrupt-history") { return 5; }
    string prepared = Directory.GetFiles(root, "prepared-*.json").Single();
    using var file = new FileStream(prepared, FileMode.Open, FileAccess.Write, FileShare.None);
    file.SetLength(0);
    file.Write(System.Text.Encoding.UTF8.GetBytes("owned child modified history"));
    file.Flush(flushToDisk: true);
    return 0;
}
Console.WriteLine("locked");
Console.Out.Flush();
return Console.ReadLine() == "release" ? 0 : 4;
