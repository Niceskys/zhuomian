using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Rendering.Wpf.Tests;

public sealed class IncrementalRendererTests
{
    private static readonly bool[] RendererOrder = [true, false];
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    private static SceneSnapshot Idle()
    {
        var d = new SpaceDispatcher(new Clock(), new(), modernFixture: true);
        return SceneSnapshot.Capture(1, d, 1, new(reducedTransparency: true));
    }
    private static void Create(IHostRenderer renderer) => renderer.Create(0, 0, 1200, 800, (_, _, _, _) => null);
    [Theory]
    [InlineData(1d)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2d)]
    public void StaticAndGeometryUpdatesReuseAllVisualsAtSyntheticDpi(double scale)
    {
        UnifiedHostTests.OnStaThread(-4, () =>
        {
            using var renderer = new WpfHostRenderer(); Create(renderer);
            var scene = Idle() with { PixelsPerDip = scale };
            renderer.Submit(scene); long created = renderer.Diagnostics.VisualCreated;
            Assert.Equal(125, created);
            for (int i = 2; i < 100; i++) { renderer.Submit(scene with { FrameId = i }); }
            Assert.Equal(created, renderer.Diagnostics.VisualCreated); Assert.Equal(1, renderer.Diagnostics.FullTreeRebuild);
            long layout = renderer.Diagnostics.LayoutInvalidated;
            renderer.Submit(scene with { FrameId = 100 }); Assert.Equal(layout, renderer.Diagnostics.LayoutInvalidated);
            var space = scene.Spaces[0]; var moved = space with { Bounds = new(space.Bounds.X + .75, space.Bounds.Y, space.Bounds.Width, space.Bounds.Height) };
            var next = scene with { FrameId = 101, Spaces = scene.Spaces.SetItem(0, moved) };
            renderer.Submit(next); Assert.Equal(created, renderer.Diagnostics.VisualCreated);
            Assert.True(renderer.MatchesSubmittedGeometry(next)); Assert.True(renderer.Diagnostics.LayoutInvalidated > layout);
            renderer.Reset(); Assert.Equal(0, renderer.Diagnostics.LiveVisuals); Assert.Null(renderer.SubmittedScene);
        });
    }
    [Fact]
    public void SingleItemTextAdditionRemovalAndSpaceRemovalReleaseOnlyOwnedObjects()
    {
        UnifiedHostTests.OnStaThread(-4, () =>
        {
            using var renderer = new WpfHostRenderer(); Create(renderer); var a = Idle(); renderer.Submit(a);
            var space = a.Spaces[0]; var item = space.Items[0];
            var changed = space with { Items = space.Items.SetItem(0, item with { Label = "Changed fake label" }) };
            renderer.Submit(a with { FrameId = 2, Spaces = a.Spaces.SetItem(0, changed) });
            Assert.Equal(125, renderer.Diagnostics.VisualCreated); Assert.Equal(0, renderer.Diagnostics.VisualRemoved);
            var extra = item with { Id = "20000000-0000-0000-0000-000000000099", Bounds = new(item.Bounds.X, item.Bounds.Y + 60, item.Bounds.Width, item.Bounds.Height) };
            var added = changed with { Items = changed.Items.Add(extra), ItemIdentities = changed.ItemIdentities.Add(extra.Id) };
            renderer.Submit(a with { FrameId = 3, Spaces = a.Spaces.SetItem(0, added) });
            Assert.Equal(129, renderer.Diagnostics.VisualCreated); Assert.Equal(129, renderer.Diagnostics.LiveVisuals);
            renderer.Submit(a with { FrameId = 4 }); Assert.Equal(4, renderer.Diagnostics.VisualRemoved); Assert.Equal(125, renderer.Diagnostics.LiveVisuals);
            renderer.Submit(a with { FrameId = 5, Spaces = a.Spaces.RemoveAt(0) });
            Assert.Equal(125 - 7 - 4 * space.Items.Length, renderer.Diagnostics.LiveVisuals);
            renderer.Reset(); Assert.Equal(renderer.Diagnostics.VisualCreated, renderer.Diagnostics.VisualRemoved);
            renderer.Submit(a with { FrameId = 6 }); Assert.Equal(125, renderer.Diagnostics.LiveVisuals);
            renderer.Dispose(); Assert.Equal(0, renderer.Diagnostics.LiveVisuals);
        });
    }
    [Fact]
    public void PartialRenderFailureClearsAllCachesAndCanBeDisposed()
    {
        UnifiedHostTests.OnStaThread(-4, () =>
        {
            using var renderer = new WpfHostRenderer(); Create(renderer); var a = Idle(); renderer.Submit(a);
            var space = a.Spaces[0]; var bad = space.Items[0] with { Id = "not-a-guid" };
            Assert.Throws<FormatException>(() => renderer.Submit(a with
            {
                FrameId = 2,
                Spaces = a.Spaces.SetItem(0, space with { Items = space.Items.Add(bad) })
            }));
            Assert.Null(renderer.SubmittedScene); Assert.Equal(0, renderer.Diagnostics.LiveVisuals);
            Assert.Equal(renderer.Diagnostics.VisualCreated, renderer.Diagnostics.VisualRemoved);
        });
    }
    private static List<(string Scenario, SceneSnapshot Scene)> Replay()
    {
        var result = new List<(string, SceneSnapshot)>(); var clock = new Clock();
        var d = new SpaceDispatcher(clock, new(), modernFixture: true);
        var monitor = new MonitorWorkArea("synthetic", new(0, 0, 1200, 800), new(1));
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic"); long frame = 0;
        void Sample(string scenario, bool motion = true)
        {
            var completions = d.RenderTargets(monitor, motion);
            result.Add((scenario, SceneSnapshot.Capture(++frame, d, 1, new(reducedTransparency: true))));
            foreach (var completion in completions) { d.Complete(completion); }
        }
        void Click(string id, bool close = false)
        { var hit = new SurfaceHit(id, Close: close); d.DispatchPointer(PointerAction.Down, hit); d.DispatchPointer(PointerAction.Up, hit); }
        Sample("idle");
        for (int i = 0; i < 500; i++)
        {
            string id = (i % 2 == 0 ? EngineeringFixture.HoverId : EngineeringFixture.ClickId).ToString("D");
            if (i % 2 == 0) { d.DispatchPointer(PointerAction.Move, new(id)); clock.NowMilliseconds += 20; }
            else { Click(id); }
            string mode = i % 2 == 0 ? "A" : "B";
            Sample(mode); clock.NowMilliseconds += 80; Sample(mode);
            // Every tenth sequence reverses from current geometry, retaining the original controller/token path.
            if (i % 10 == 0) { Click(id, true); Sample("reverse"); }
            else { clock.NowMilliseconds += 240; Sample(mode); Click(id, true); Sample("return"); }
            clock.NowMilliseconds += 240; Sample("return"); d.DispatchPointer(PointerAction.Leave);
            Assert.Null(d.Controller.FocusOwner); Assert.False(d.HasActiveAnimation);
        }
        string b = EngineeringFixture.ClickId.ToString("D"); Click(b); Sample("reduced-motion", false); Click(b, true); Sample("reduced-motion", false);
        d.HostLost(); Assert.False(d.HasActiveAnimation); d.HostRestored(); Sample("host-restored", false);
        return result;
    }
    [Fact]
    public void FiveHundredAuthoritativeTransitionsHaveBoundedCachesAndSameProtocolBeforeAfterData()
    {
        UnifiedHostTests.OnStaThread(-4, () =>
        {
            var replay = Replay(); var rows = new List<object>(); var resources = new List<object>();
            static object ResourceSample(string phase, int repetition)
            {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                return new
                {
                    phase,
                    repetition,
                    privateBytes = process.PrivateMemorySize64,
                    workingSet = process.WorkingSet64,
                    handles = process.HandleCount,
                    threads = process.Threads.Count
                };
            }
            resources.Add(ResourceSample("before-comparison", -1));
            // Same scene objects, hidden viewport, owner STA, warmup and measurement protocol.
            // Three engineering repetitions; no GPU/display/60FPS inference.
            for (int repetition = 0; repetition < 3; repetition++)
            {
                foreach (bool baseline in RendererOrder)
                {
                    using IHostRenderer renderer = baseline ? new BaselineWpfRenderer() : new WpfHostRenderer(); Create(renderer);
                    for (int warmup = 0; warmup < 5; warmup++) { renderer.Submit(replay[0].Scene with { FrameId = -warmup }); }
                    var incremental = renderer as WpfHostRenderer;
                    for (int i = 0; i < replay.Count; i++)
                    {
                        var (scenario, scene) = replay[i];
                        long created = incremental?.Diagnostics.VisualCreated ?? 0;
                        long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
                        renderer.Submit(scene);
                        long ticks = Stopwatch.GetTimestamp() - started, bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                        long createdDelta = incremental is null ? scene.Spaces.Sum(s => 2 + (s.Expanded ? 5 : 0) + s.Items.Length * 4) : incremental.Diagnostics.VisualCreated - created;
                        if (incremental is not null)
                        {
                            Assert.Equal(0, createdDelta); Assert.Equal(125, incremental.Diagnostics.LiveVisuals);
                            Assert.Equal(1, incremental.Diagnostics.FullTreeRebuild);
                            Assert.True(incremental.MatchesSubmittedGeometry(scene), $"frame={i} scenario={scenario} {incremental.GeometryMismatch}");
                        }
                        rows.Add(new
                        {
                            repetition,
                            renderer = baseline ? "UI-I1-rebuild" : "UI-I2-incremental",
                            frame = i,
                            scenario,
                            created = createdDelta,
                            ticks,
                            allocatedBytes = bytes,
                            retainedVisuals = incremental?.Diagnostics.LiveVisuals ?? createdDelta,
                            totalVisualReused = incremental?.Diagnostics.VisualReused,
                            totalVisualRemoved = incremental?.Diagnostics.VisualRemoved,
                            totalLayoutInvalidated = incremental?.Diagnostics.LayoutInvalidated,
                            totalFullTreeRebuild = incremental?.Diagnostics.FullTreeRebuild,
                            totalSceneSubmitted = incremental?.Diagnostics.SceneSubmitted
                        });
                    }
                    if (incremental is not null) { incremental.Reset(); Assert.Equal(0, incremental.Diagnostics.LiveVisuals); }
                    renderer.Dispose(); resources.Add(ResourceSample(baseline ? "after-baseline-dispose" : "after-incremental-dispose", repetition));
                }
            }
            string? directory = Environment.GetEnvironmentVariable("ZHUOMIAN_RENDER_DIAGNOSTICS");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "render-comparison.json"), JsonSerializer.Serialize(new
                {
                    kind = "Windows-CI-engineering-diagnostic",
                    protocol = "hidden HwndSource 1200x800 PMv2 STA; 5 idle warmups; 500 alternating A/B cycles with every tenth reversal; 3 repetitions; sequential baseline then incremental; same immutable scenes; CPU Submit only",
                    baselineSource = "74b497da2d3993e58f1c3a788a86faea37453038",
                    stopwatchFrequency = Stopwatch.Frequency,
                    gpu = "UNVERIFIED",
                    presentedFrames = "UNVERIFIED",
                    rows
                }));
            }
        }, timeoutSeconds: 180);
    }
}
