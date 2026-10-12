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
    private readonly ConnectedWheelSelection _selection = new();

    public WheelSelectionService(IReadOnlyList<WheelDefinition> definitions,
        Func<IReadOnlyList<WheelInterface>>? discover = null)
    {
        _definitions = definitions;
        _discover = discover ?? Discover;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    // This ID identifies a connected physical instance, never a definition/model.
    public string? ActiveWheelId => _selection.ActiveWheelId;
    public ConnectedWheel? ActiveDevice => _selection.ActiveDevice;
    public IReadOnlyList<ConnectedWheel> ConnectedDevices => _selection.ConnectedDevices;
    public WheelDeviceSnapshot Snapshot => _selection.Snapshot;
    public event EventHandler<string?>? ActiveWheelChanged { add => _selection.ActiveWheelChanged += value; remove => _selection.ActiveWheelChanged -= value; }
    public event EventHandler<WheelDeviceSnapshot>? DevicesChanged { add => _selection.DevicesChanged += value; remove => _selection.DevicesChanged -= value; }
    public void Start() { Refresh(); _timer.Start(); }
    public void Dispose() => _timer.Stop();
    public void SetPreferredWheel(string? instanceId) { _selection.PreferredInstanceId = instanceId; Refresh(); }
    public void Refresh()
    {
        try { _selection.Update(_discover()); }
        catch { /* A failed enumeration is not evidence of a disconnect. */ }
    }
    public static IReadOnlyList<ConnectedWheel> GroupInterfaces(IReadOnlyList<WheelInterface> interfaces) => ConnectedWheelSelection.GroupInterfaces(interfaces);

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
