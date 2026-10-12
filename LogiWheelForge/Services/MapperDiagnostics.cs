using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed record MapperDiagnosticsSnapshot(IReadOnlyList<WheelInputSample> Inputs,
    IReadOnlyList<MappingActivity> Live, IReadOnlyList<MappingActivity> Preview);

/// <summary>Bounded latest-per-control/rule diagnostics shared by mounted views.</summary>
public sealed class MapperDiagnostics
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WheelInputSample> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MappingActivity> _live = [], _preview = [];
    public void Input(WheelInputSample sample) { lock (_gate) _inputs[sample.Control] = sample; }
    public void Activity(MappingActivity activity, bool preview)
    {
        lock (_gate) (preview ? _preview : _live)[activity.ProfileId + ":" + activity.RuleId] = activity;
    }
    public void ClearInputs() { lock (_gate) _inputs.Clear(); }
    public void ClearPreview() { lock (_gate) _preview.Clear(); }
    public void KeepProfiles(IReadOnlyList<InputMapperProfile> profiles)
    {
        var ids = profiles.SelectMany(p => p.Rules.Select(r => p.Id + ":" + r.Id)).ToHashSet();
        lock (_gate) foreach (var id in _live.Keys.Where(id => !ids.Contains(id)).ToArray()) _live.Remove(id);
    }
    public MapperDiagnosticsSnapshot Snapshot()
    {
        lock (_gate) return new(_inputs.Values.ToArray(), _live.Values.ToArray(), _preview.Values.ToArray());
    }
}

// Preview has no references to SendInput or DirectInput. These adapters only acknowledge simulated operations.
public sealed class InertMapperOutput : IMapperOutput
{
    public string? LastError => null;
    public bool Press(string output, bool mouseButton = false) => true;
    public bool Release(string output, bool mouseButton = false) => true;
    public void ReleaseAll() { }
    public bool Move(int x, int y) => true;
    public bool Scroll(int clicks) => true;
}
public sealed class InertWheelResistance : IWheelResistanceController
{
    public bool IsAvailable => true;
    public string Status => "Preview · no physical resistance";
    public bool Start(string instanceId, MapperResistance settings) => true;
    public void UpdatePosition(double steeringPercent) { }
    public void Hold(double targetPercent, double strength) { }
    public void ReleaseHold() { }
    public void Stop() { }
    public void Dispose() { }
}
