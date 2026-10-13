using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class MapperRuntimeRegressionTests
{
    [DataTestMethod]
    [DataRow("Y")][DataRow("Vertical")][DataRow("vertical")]
    public void VerticalMouseOutput_UsesYAxisForLegacyAndCanonicalValues(string axis)
    {
        var output = new FakeOutput(); var engine = new MapperRuleEngine(output, new InertWheelResistance());
        engine.Activate(new() { Rules = [new() { Trigger = MapperTriggerKind.AxisRange, Action = MapperActionKind.MouseMove,
            Output = axis, OutputScale = 20 }] }, "instance");
        engine.Update(new("Steering", 50, false)); engine.Tick();
        Assert.AreEqual((0, 10), output.LastMove);
    }
    [TestMethod]
    public void StepMouseOutput_NormalizesLegacyYAndUsesVerticalMovement()
    {
        var output = new FakeOutput(); var engine = new MapperRuleEngine(output, new InertWheelResistance());
        engine.Activate(new() { Rules = [new() { Action = MapperActionKind.MouseMove, Output = "Y", OutputScale = 7 }] }, "instance");
        engine.Update(new("Steering", 0, false)); engine.Update(new("Steering", 5, false));
        Assert.AreEqual((0, 7), output.LastMove);
    }
    [DataTestMethod]
    [DataRow("LeftCtrl+K", true)][DataRow("Space", true)][DataRow("LeftCtrl++K", false)]
    [DataRow("NotAKey", false)][DataRow("None", false)][DataRow("1", false)][DataRow("K+K", false)]
    public void KeyValidation_RejectsMalformedOrUnavailableShortcuts(string value, bool valid) =>
        Assert.AreEqual(valid, new MapperRule { Output = value }.TryValidate(out _));

    [TestMethod]
    public async Task Store_NormalizesAxesAndRetainsApplicationsAndRuleIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new InputMapperProfileStore(path);
            var profile = new InputMapperProfile { ProcessPaths = [Path.GetFullPath("first.exe"), Path.GetFullPath("second.exe")],
                Rules = [new() { Action = MapperActionKind.MouseMove, Output = "Y" }] };
            await store.SaveAsync([profile]); var loaded = (await store.LoadAsync()).Single();
            Assert.AreEqual("Vertical", loaded.Rules[0].Output); Assert.AreEqual(profile.Id, loaded.Id);
            Assert.AreEqual(profile.Rules[0].Id, loaded.Rules[0].Id); CollectionAssert.AreEqual(profile.ProcessPaths, loaded.ProcessPaths);
        }
        finally { File.Delete(path); }
    }
    [TestMethod]
    public void CachedDevices_CanBeReselectedAfterWheelSwitch()
    {
        using var source = new WheelInputSource([], id => id is null ? [] : id == "first" ? ["Steering", "Button1"] : ["Steering", "Button2"]);
        var resolved = 0;
        WheelInputSource.ParsedDevice? Resolve(nint handle)
        {
            resolved++;
            var id = handle == 1 ? "first" : "second";
            return new(id + "/input", "g29", id, null!, [], false, true);
        }
        source.SetPreferredWheel("first"); Assert.IsNotNull(source.ResolveAndSelect(1, Resolve));
        Assert.AreEqual("first", source.ActiveWheelId);
        source.SetPreferredWheel("second"); Assert.IsNull(source.ResolveAndSelect(1, Resolve));
        Assert.IsNotNull(source.ResolveAndSelect(2, Resolve)); Assert.AreEqual("second", source.ActiveWheelId);
        CollectionAssert.AreEquivalent(new[] { "Steering", "Button2" }, source.AvailableControls.ToArray());
        source.SetPreferredWheel("first"); Assert.IsNotNull(source.ResolveAndSelect(1, Resolve));
        Assert.AreEqual("first", source.ActiveWheelId); Assert.AreEqual(2, resolved, "Switching back must select the already cached handle.");
        CollectionAssert.AreEquivalent(new[] { "Steering", "Button1" }, source.AvailableControls.ToArray());
        source.SetPreferredWheel(null); Assert.IsNull(source.ResolveAndSelect(1, Resolve)); Assert.AreEqual(0, source.AvailableControls.Count);
    }
    [TestMethod]
    public void Diagnostics_DistinguishQueuedHeldReleasedAndFailedOutputs()
    {
        var clock = new TestClock(); var output = new FakeOutput(); var engine = new MapperRuleEngine(output, new InertWheelResistance(), clock);
        var profile = new InputMapperProfile { Rules = [new()] }; var events = new List<MappingActivity>();
        engine.ActivityChanged += (_, activity) => events.Add(activity);
        engine.Activate(profile, "wheel"); engine.Update(new("Steering", 0, false)); engine.Update(new("Steering", 5, false));
        Assert.IsTrue(events.Any(e => e.State == MappingOutputState.Triggered));
        Assert.IsTrue(events.Any(e => e.State == MappingOutputState.Queued));
        engine.Tick(); Assert.AreEqual(MappingOutputState.Held, events[^1].State);
        clock.Advance(60); engine.Tick(); Assert.AreEqual(MappingOutputState.Released, events[^1].State);
        output.Fail = true; engine.Update(new("Steering", 10, false)); engine.Tick();
        Assert.AreEqual(MappingOutputState.Failed, events[^1].State);
        Assert.AreEqual(profile.Id, events[^1].ProfileId); Assert.AreEqual(profile.Rules[0].Id, events[^1].RuleId);
    }
    [TestMethod]
    public void SaturatedTapQueue_IsBoundedAndExplainsDroppedOutputs()
    {
        var engine = new MapperRuleEngine(new FakeOutput(), new InertWheelResistance());
        var events = new List<MappingActivity>(); engine.ActivityChanged += (_, activity) => events.Add(activity);
        engine.Activate(new() { Rules = [new() { StepPercent = 1 }] }, "wheel");
        engine.Update(new("Steering", 0, false)); engine.Update(new("Steering", 20, false));
        Assert.AreEqual(8, engine.DroppedPulses);
        Assert.AreEqual(12, events.Count(a => a.State == MappingOutputState.Queued));
        Assert.IsTrue(events.Where(a => a.State == MappingOutputState.Failed).All(a => a.Error!.Contains("Tap queue is full")));
    }

    [TestMethod]
    public async Task ForegroundGuard_AndDisableDisconnectProfileChangesReleaseOutputs()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new InputMapperProfileStore(path); var output = new FakeOutput(); var input = new FakeSource();
            var profile = new InputMapperProfile { ProcessPaths = [Path.GetFullPath("target.exe")],
                Rules = [new() { Trigger = MapperTriggerKind.AxisRange, Mode = MapperActionMode.Hold, Output = "W" }] };
            await store.SaveAsync([profile]);
            var foreground = new MapperForegroundSnapshot(2, profile.ProcessPaths[0]);
            using var service = new InputMapperService([], store, new InertWheelResistance(), output, input,
                foreground: () => foreground);
            await service.InitializeAsync(1, "wheel-one"); service.Start();
            input.Emit("Steering", 20); Assert.AreEqual(1, output.Presses);
            foreground = new(1, profile.ProcessPaths[0]); input.Emit("Steering", 30); // Own window is always guarded.
            Assert.AreEqual(1, output.Presses); Assert.AreEqual(0, output.Held);
            foreground = new(2, Path.GetFullPath("unrelated.exe")); input.Emit("Steering", 30); Assert.AreEqual(1, output.Presses);
            foreground = new(2, profile.ProcessPaths[0]); input.Emit("Steering", 30); Assert.AreEqual(1, output.Held);
            await service.SaveProfilesAsync([profile with { Rules = [profile.Rules[0] with { Output = "S" }] }]);
            Assert.AreEqual(0, output.Held); input.Emit("Steering", 30); Assert.AreEqual("S", output.LastKey);
            service.SetPreferredWheel("wheel-two"); Assert.AreEqual(0, output.Held); Assert.AreEqual("wheel-two", input.ActiveWheelId);
            input.Emit("Steering", 30); input.Disconnect(); Assert.AreEqual(0, output.Held);
            service.Stop(); Assert.AreEqual(0, output.Held);
        }
        finally { File.Delete(path); }
    }
    [TestMethod]
    public async Task InactiveProfileEdits_AndRejectedWritesDoNotInterruptLiveProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "profiles.json"); var store = new InputMapperProfileStore(path);
            var active = new InputMapperProfile { ProcessPaths = [Path.GetFullPath("target.exe")],
                Rules = [new() { Trigger = MapperTriggerKind.AxisRange, Mode = MapperActionMode.Hold }] };
            var inactive = new InputMapperProfile { Name = "Other" }; await store.SaveAsync([active, inactive]);
            var output = new FakeOutput(); var input = new FakeSource();
            using var service = new InputMapperService([], store, new InertWheelResistance(), output, input,
                foreground: () => new(2, active.ProcessPaths[0]));
            await service.InitializeAsync(1, "wheel"); service.Start(); input.Emit("Steering", 50);
            var releases = output.Releases;
            await service.SaveProfilesAsync([active, inactive with { Name = "Edited" }]);
            Assert.AreEqual(releases, output.Releases); Assert.AreEqual(1, output.Held);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.SaveProfilesAsync([active with { Name = "" }, inactive]));
            Assert.AreEqual(releases, output.Releases); Assert.AreEqual(active.Name, service.Profiles[0].Name);
            File.Delete(path); Directory.CreateDirectory(path); // Fail the atomic rename after serialization.
            try { await service.SaveProfilesAsync([active with { Name = "Cannot write" }, inactive]); Assert.Fail("Expected write failure"); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            Assert.AreEqual(releases, output.Releases); Assert.AreEqual(active.Name, service.Profiles[0].Name);
        }
        finally { Directory.Delete(directory, true); }
    }
    [TestMethod]
    public async Task Preview_UsesInertAdaptersAndDoesNotCallLiveOutputsOrResistance()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var source = new FakeSource(); var output = new FakeOutput(); var force = new FakeForce();
        using var service = new InputMapperService([], new(path), force, output, source, foreground: () => new(1, null));
        await service.InitializeAsync(1, "wheel");
        service.SetPreviewProfile(new() { Resistance = new() { Enabled = true }, Rules = [new() { Trigger = MapperTriggerKind.AxisRange,
            Mode = MapperActionMode.Hold }, new() { Trigger = MapperTriggerKind.AxisRange, Action = MapperActionKind.HoldTarget }] });
        var releases = output.Releases;
        source.Emit("Steering", 20); service.Tick();
        Assert.AreEqual(0, output.Presses); Assert.AreEqual(releases, output.Releases); Assert.AreEqual(0, force.Starts); Assert.AreEqual(0, force.Holds);
        Assert.IsTrue(service.Diagnostics.Snapshot().Preview.Any(a => a.State == MappingOutputState.Held));
        Assert.AreEqual(0, service.Diagnostics.Snapshot().Live.Count);
    }
    [TestMethod]
    public void DraftValidation_RetainsNumericTextAndNormalizesMouseChoices()
    {
        var draft = new MapperRuleDraft(new() { Action = MapperActionKind.MouseMove, Output = "Y" });
        Assert.AreEqual("Vertical", draft.Output); CollectionAssert.AreEqual(new[] { "Horizontal", "Vertical" }, draft.OutputChoices.ToArray());
        draft.StepPercentText = "unresolved";
        Assert.AreEqual("unresolved", draft.StepPercentText); Assert.ThrowsException<ArgumentException>(() => draft.Build());
        Assert.AreEqual(5d, draft.StepPercent);
        draft.StepPercentText = "10"; Assert.AreEqual(10d, draft.Build().StepPercent);
    }
    [TestMethod]
    public async Task OverviewNavigation_AndStarterMappingsPreserveMultipleApplications()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            using var service = new InputMapperService([], new(path), new InertWheelResistance(), new InertMapperOutput(), new FakeSource(), foreground: () => new(1, null));
            using var viewModel = new InputMapperViewModel(service); viewModel.Initialize(); Assert.IsFalse(viewModel.IsEditorOpen);
            viewModel.AddProfileCommand.Execute(null); Assert.IsTrue(viewModel.IsEditorOpen); Assert.AreEqual(1, viewModel.EditorTabIndex);
            var profile = viewModel.SelectedProfile!; Assert.AreEqual(4, profile.Rules.Count);
            CollectionAssert.AreEqual(new[] { "D", "A", "W", "S" }, profile.Rules.Select(r => r.Output).ToArray());
            profile.ProcessPaths.Add(Path.GetFullPath("first.exe")); profile.ProcessPaths.Add(Path.GetFullPath("second.exe"));
            viewModel.BackCommand.Execute(null); Assert.IsFalse(viewModel.IsEditorOpen);
            viewModel.OpenProfileCommand.Execute(profile); Assert.AreSame(profile, viewModel.SelectedProfile);
            await viewModel.SaveChangesAsync(); Assert.AreEqual("Saved", viewModel.EditStatus); Assert.AreEqual(2, service.Profiles[0].ProcessPaths.Length);
        }
        finally { File.Delete(path); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int milliseconds) => _now = _now.AddMilliseconds(milliseconds);
    }
    private sealed class FakeSource : IWheelInputSource
    {
        public event EventHandler<WheelInputSample>? InputReceived;
        public event EventHandler<string>? ErrorOccurred { add { } remove { } }
        public event EventHandler<string?>? ActiveWheelChanged;
        public string? ActiveWheelId { get; private set; }
        public IReadOnlyCollection<string> AvailableControls { get; private set; } = [];
        public void Initialize(nint handle) { }
        public void SetPreferredWheel(string? id) { ActiveWheelId = id; AvailableControls = id is null ? [] : ["Steering"]; ActiveWheelChanged?.Invoke(this, id); }
        public void CheckPresence() { }
        public void Emit(string control, double percent) => InputReceived?.Invoke(this, new(control, percent, false));
        public void Disconnect() => SetPreferredWheel(null);
        public void Dispose() { }
    }
    private sealed class FakeOutput : IMapperOutput
    {
        public bool Fail { get; set; }
        public string? LastError => Fail ? "Rejected output" : null;
        public int Presses, Releases, Held;
        public string? LastKey;
        public (int X, int Y) LastMove;
        public bool Press(string output, bool mouseButton = false) { if (Fail) return false; Presses++; Held++; LastKey = output; return true; }
        public bool Release(string output, bool mouseButton = false) { Held = Math.Max(0, Held - 1); return !Fail; }
        public void ReleaseAll() { Releases++; Held = 0; }
        public bool Move(int x, int y) { LastMove = (x, y); return !Fail; }
        public bool Scroll(int clicks) => !Fail;
    }
    private sealed class FakeForce : IWheelResistanceController
    {
        public int Starts, Holds;
        public bool IsAvailable => true;
        public string Status => "Live resistance";
        public bool Start(string id, MapperResistance settings) { Starts++; return true; }
        public void Hold(double target, double strength) => Holds++;
        public void UpdatePosition(double percent) { }
        public void ReleaseHold() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
