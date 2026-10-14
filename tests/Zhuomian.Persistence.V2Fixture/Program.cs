using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence;

if (args.Length != 3 || !Enum.TryParse<V2Stage>(args[1], out var stage)) { return 2; }
using var store = WorkspaceV2Store.ForTests(args[0], args[2] == "load" ? null : new Pause(stage));
if (args[2] == "load")
{
    var loaded = store.Load();
    Console.WriteLine($"{loaded.Error}:{loaded.Workspace?.Revision}");
    return loaded.Error == StorageError.None ? 0 : 1;
}
var previous = store.Load().Workspace;
if (previous is null) { return 3; }
var next = Workspace.FromSnapshot(previous.Id, previous.SchemaVersion, 10, previous.ItemStore,
    previous.Spaces.Values, previous.RemovedSpaces.Values);
var saved = store.Save(next);
Console.WriteLine($"{saved.Error}:{saved.Commit}");
return saved.Succeeded ? 0 : 1;

sealed class Pause(V2Stage target) : IV2FaultInjector
{
    public void Check(V2Stage stage)
    {
        if (stage != target) { return; }
        Console.WriteLine("cut"); Console.Out.Flush();
        using var gate = new ManualResetEvent(false);
        gate.WaitOne();
    }
}
