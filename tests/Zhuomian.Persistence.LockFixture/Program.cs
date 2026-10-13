// Repository-owned no-resource fixture. Parent supplies only its isolated temporary test root.
if (args.Length != 1) { return 2; }
string root = Path.GetFullPath(args[0]);
if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal) ||
    !Path.GetFileName(root).StartsWith("zhuomian-persistence-", StringComparison.Ordinal) || !Directory.Exists(root)) { return 3; }
using var mutex = new FileStream(Path.Combine(root, "workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
Console.WriteLine("locked");
Console.Out.Flush();
return Console.ReadLine() == "release" ? 0 : 4;
