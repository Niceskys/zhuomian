using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence;

if (args.Length is not (3 or 4) || !Enum.TryParse<V2Stage>(args[1], out var stage)) { return 2; }
string mode = args[2];
bool recoverable = mode.StartsWith("v3-", StringComparison.Ordinal);
if (recoverable) { mode = mode[3..]; }
var fault = mode == "load" ? null : new Pause(stage, args.Length == 4 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0);
using var store = recoverable ? WorkspaceV2Store.ForRecoverableTests(args[0], fault) : WorkspaceV2Store.ForTests(args[0], fault);
if (mode == "recover") { return store.RecoverAuthority().Succeeded ? 0 : 1; }
if (mode == "load")
{
    var loaded = store.Load();
    Console.WriteLine($"{loaded.Error}:{loaded.Workspace?.Revision}");
    return loaded.Error == StorageError.None ? 0 : 1;
}
if (mode == "initialize")
{
    var empty = Workspace.Create(new Guid("00000001-0000-0000-0000-000000000000"));
    var initial = Workspace.FromSnapshot(empty.Id, empty.SchemaVersion, 10, empty.ItemStore, empty.Spaces.Values);
    return store.InitializeNew(initial).Succeeded ? 0 : 1;
}
if (mode == "restore") { return store.RestoreRevision(0).Error == StorageError.None ? 0 : 1; }
var previous = store.Load().Workspace;
if (previous is null) { return 3; }
var next = Workspace.FromSnapshot(previous.Id, previous.SchemaVersion, 10, previous.ItemStore,
    previous.Spaces.Values, previous.RemovedSpaces.Values);
var saved = store.Save(next);
Console.WriteLine($"{saved.Error}:{saved.Commit}");
return saved.Succeeded ? 0 : 1;

sealed class Pause(V2Stage target, int write) : IV2FaultInjector
{
    private int _writes;
    public void Check(V2Stage stage)
    {
        if (stage == V2Stage.BeforeWrite) { _writes++; }
        if (stage != target || (write != 0 && _writes != write)) { return; }
        Console.WriteLine("cut"); Console.Out.Flush();
        using var gate = new ManualResetEvent(false);
        gate.WaitOne();
    }
}
