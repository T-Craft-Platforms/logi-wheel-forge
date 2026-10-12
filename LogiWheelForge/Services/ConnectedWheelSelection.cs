using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

/// <summary>Deterministic selection of physical wheels, independent of discovery and presentation clocks.</summary>
public sealed class ConnectedWheelSelection
{
    public string? PreferredInstanceId { get; set; }
    public string? ActiveWheelId => ActiveDevice?.InstanceId;
    public ConnectedWheel? ActiveDevice { get; private set; }
    public IReadOnlyList<ConnectedWheel> ConnectedDevices { get; private set; } = [];
    public WheelDeviceSnapshot Snapshot => new(ConnectedDevices, ActiveDevice);
    public event EventHandler<string?>? ActiveWheelChanged;
    public event EventHandler<WheelDeviceSnapshot>? DevicesChanged;
    public void Update(IReadOnlyList<WheelInterface> interfaces)
    {
        var wheels = GroupInterfaces(interfaces);
        var selected = wheels.FirstOrDefault(w => w.InstanceId.Equals(PreferredInstanceId, StringComparison.OrdinalIgnoreCase))
                       ?? wheels.FirstOrDefault(w => w.InstanceId.Equals(ActiveWheelId, StringComparison.OrdinalIgnoreCase))
                       ?? wheels.FirstOrDefault();
        // A stale persisted preference must not reclaim selection after a replacement was chosen.
        if (PreferredInstanceId is not null && selected?.InstanceId != PreferredInstanceId) PreferredInstanceId = selected?.InstanceId;
        var changed = selected?.InstanceId != ActiveWheelId;
        var membershipChanged = !ConnectedDevices.Select(Fingerprint).SequenceEqual(wheels.Select(Fingerprint));
        if (!membershipChanged)
        {
            selected = ConnectedDevices.FirstOrDefault(w => w.InstanceId == selected?.InstanceId);
        }
        else ConnectedDevices = wheels;
        ActiveDevice = selected;
        if (changed) ActiveWheelChanged?.Invoke(this, ActiveWheelId);
        if (changed || membershipChanged) DevicesChanged?.Invoke(this, Snapshot);
    }

    private static string Fingerprint(ConnectedWheel wheel) => wheel.InstanceId + "|" + wheel.PickerLabel + "|" + string.Join("|", wheel.HidInterfaces);

    public static IReadOnlyList<ConnectedWheel> GroupInterfaces(IReadOnlyList<WheelInterface> interfaces)
    {
        var wheels = interfaces.Where(i => !i.IsPedal).GroupBy(i => i.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(i => i.IsWheel))
            .Select(group =>
            {
                var primary = group.First(i => i.IsWheel);
                return new ConnectedWheel(primary.InstanceId, primary.DisplayName, primary.DefinitionId,
                    group.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(), primary.InstanceId);
            }).OrderBy(w => w.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var group in wheels.GroupBy(w => w.DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var number = 0;
            foreach (var wheel in group)
            {
                var index = Array.IndexOf(wheels, wheel);
                wheels[index] = wheel with { PickerLabel = $"{wheel.DisplayName} · {++number}" };
            }
        }
        return wheels;
    }

}
