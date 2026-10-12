using System.Windows.Threading;
using HidSharp;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

/// <summary>One shared selection for input, LEDs, resistance and presentation; pedals are auxiliary.</summary>
public sealed class WheelSelectionService : IDisposable
{
    private readonly IReadOnlyList<WheelDefinition> _definitions;
    private readonly DispatcherTimer _timer;
    private readonly Func<IReadOnlyList<WheelInterface>> _discover;
    private string? _preferred;

    public WheelSelectionService(IReadOnlyList<WheelDefinition> definitions,
        Func<IReadOnlyList<WheelInterface>>? discover = null)
    {
        _definitions = definitions;
        _discover = discover ?? Discover;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    // This ID identifies a connected physical instance, never a definition/model.
    public string? ActiveWheelId => ActiveDevice?.InstanceId;
    public ConnectedWheel? ActiveDevice { get; private set; }
    public IReadOnlyList<ConnectedWheel> ConnectedDevices { get; private set; } = [];
    public WheelDeviceSnapshot Snapshot => new(ConnectedDevices, ActiveDevice);
    public event EventHandler<string?>? ActiveWheelChanged;
    public event EventHandler<WheelDeviceSnapshot>? DevicesChanged;
    public void Start() { Refresh(); _timer.Start(); }
    public void Dispose() => _timer.Stop();
    public void SetPreferredWheel(string? instanceId) { _preferred = instanceId; Refresh(); }

    public void Refresh()
    {
        IReadOnlyList<WheelInterface> interfaces;
        try { interfaces = _discover(); }
        catch { return; } // A failed enumeration is not evidence of a disconnect.
        var wheels = GroupInterfaces(interfaces);
        var selected = wheels.FirstOrDefault(w => w.InstanceId.Equals(_preferred, StringComparison.OrdinalIgnoreCase))
                       ?? wheels.FirstOrDefault(w => w.InstanceId.Equals(ActiveWheelId, StringComparison.OrdinalIgnoreCase))
                       ?? wheels.FirstOrDefault();
        // A stale persisted preference must not reclaim selection after a replacement was chosen.
        if (_preferred is not null && selected?.InstanceId != _preferred) _preferred = selected?.InstanceId;
        var changed = selected?.InstanceId != ActiveWheelId;
        var membershipChanged = !ConnectedDevices.Select(Fingerprint).SequenceEqual(wheels.Select(Fingerprint));
        ConnectedDevices = wheels;
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

    private IReadOnlyList<WheelInterface> Discover()
    {
        NativeWheelIdentity.Refresh();
        var interfaces = new List<WheelInterface>();
        foreach (var device in DeviceList.Local.GetHidDevices(0x046d))
        {
            try
            {
                var definition = _definitions.FirstOrDefault(d => d.ProductIds.Contains(device.ProductID));
                string name;
                try { name = device.GetProductName(); } catch { name = string.Empty; }
                var pedal = definition?.IsPedalSet == true || name.Contains("pedal", StringComparison.OrdinalIgnoreCase);
                if (definition is null && !pedal && !name.Contains("wheel", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("racing", StringComparison.OrdinalIgnoreCase)) continue;
                var descriptor = device.GetReportDescriptor();
                var wheel = !pedal && descriptor.DeviceItems.Any(item =>
                    item.Usages.GetAllValues().Any(usage => (uint)usage is 0x00010004 or 0x00010005 or 0x00010008) &&
                    item.InputReports.SelectMany(report => report.DataItems).Any(data =>
                        data.Usages.GetAllValues().Any(usage => (uint)usage == 0x00010030)));
                interfaces.Add(new WheelInterface(NativeWheelIdentity.GetInstanceId(device.DevicePath), device.DevicePath,
                    definition?.DisplayName ?? name, definition?.Id ?? $"logitech-{device.ProductID:x4}", wheel, pedal));
            }
            catch { /* Ignore inaccessible interfaces; keep enumerating the other collections. */ }
        }
        return interfaces;
    }
}
