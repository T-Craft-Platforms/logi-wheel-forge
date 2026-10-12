using System.Runtime.InteropServices;
using System.Windows.Interop;
using HidSharp;
using HidSharp.Reports;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed record WheelInputSample(string Control, double Percent, bool IsButton);

/// <summary>Reads shared Windows raw HID input; it does not take exclusive device ownership.</summary>
public interface IWheelInputSource : IDisposable
{
    event EventHandler<WheelInputSample>? InputReceived;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler<string?>? ActiveWheelChanged;
    string? ActiveWheelId { get; }
    IReadOnlyCollection<string> AvailableControls { get; }
    void Initialize(nint windowHandle);
    void SetPreferredWheel(string? instanceId);
    void CheckPresence();
}

public sealed class WheelInputSource : IWheelInputSource
{
    private const int WmInput = 0x00ff;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceInfo = 0x2000000b;
    private const uint RidevInputSink = 0x00000100;
    private readonly Dictionary<nint, ParsedDevice?> _devices = [];
    private readonly IReadOnlyList<WheelDefinition> _definitions;
    private readonly Func<string?, IReadOnlyCollection<string>>? _discoverControls;
    private HwndSource? _source;
    private string? _selectedInstanceId;
    private string? _activeDevicePath;
    private string? _activePedalPath;

    internal sealed record ParsedDevice(string Path, string WheelId, string InstanceId, ReportDescriptor Descriptor,
        IReadOnlyList<WheelInputControl> Controls, bool IsPedal, bool IsPrimaryWheel);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }

    public WheelInputSource(IReadOnlyList<WheelDefinition> definitions, Func<string?, IReadOnlyCollection<string>>? discoverControls = null)
    { _definitions = definitions; _discoverControls = discoverControls; }
    public event EventHandler<WheelInputSample>? InputReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string?>? ActiveWheelChanged;
    public string? ActiveWheelId { get; private set; }
    public IReadOnlyCollection<string> AvailableControls { get; private set; } = [];

    public void Initialize(nint windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("Window is unavailable.");
        _source.AddHook(WndProc);
        var registrations = new[] { 4, 5, 8 }.Select(usage => new RawInputDevice
            { UsagePage = 1, Usage = (ushort)usage, Flags = RidevInputSink, Target = windowHandle }).ToArray();
        if (!RegisterRawInputDevices(registrations, (uint)registrations.Length,
                (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            _source.RemoveHook(WndProc);
            _source = null;
            throw new InvalidOperationException($"Could not register wheel input: {Marshal.GetLastWin32Error()}");
        }
    }

    public void SetPreferredWheel(string? wheelId)
    {
        if (string.Equals(_selectedInstanceId, wheelId, StringComparison.OrdinalIgnoreCase)) return;
        _selectedInstanceId = wheelId;
        _activeDevicePath = null;
        _activePedalPath = null;
        AvailableControls = [];
        RefreshControls();
        ActiveWheelId = null;
        ActiveWheelChanged?.Invoke(this, null);
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            var registrations = new[] { 4, 5, 8 }.Select(usage => new RawInputDevice
                { UsagePage = 1, Usage = (ushort)usage, Flags = 1, Target = 0 }).ToArray();
            RegisterRawInputDevices(registrations, (uint)registrations.Length,
                (uint)Marshal.SizeOf<RawInputDevice>());
        }
        _devices.Clear();
    }

    public void CheckPresence()
    {
        RefreshControls();
        foreach (var handle in _devices.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray()) _devices.Remove(handle);
        if (_activeDevicePath is null && _activePedalPath is null) return;
        try
        {
            var paths = DeviceList.Local.GetHidDevices(0x046d).Select(device => device.DevicePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_activePedalPath is not null && !paths.Contains(_activePedalPath))
            {
                _activePedalPath = null;
                _devices.Clear();
                ActiveWheelChanged?.Invoke(this, ActiveWheelId);
            }
            foreach (var handle in _devices.Where(pair => pair.Value is null || !paths.Contains(pair.Value.Path))
                         .Select(pair => pair.Key).ToArray()) _devices.Remove(handle);
            if (_activeDevicePath is null || paths.Contains(_activeDevicePath)) return;
        }
        catch { return; }
        _activeDevicePath = null;
        ActiveWheelId = null;
        _devices.Clear();
        ActiveWheelChanged?.Invoke(this, null);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmInput) return 0;
        uint size = 0;
        if (GetRawInputData(lParam, RidInput, nint.Zero, ref size, (uint)(8 + 2 * IntPtr.Size)) != 0 ||
            size < 32 || size > 65536) return 0;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, (uint)(8 + 2 * IntPtr.Size)) == uint.MaxValue ||
                Marshal.ReadInt32(buffer) != 2) return 0;
            var deviceHandle = Marshal.ReadIntPtr(buffer, 8);
            var device = ResolveAndSelect(deviceHandle, ResolveDevice);
            if (device is null) return 0;
            var reportLength = Marshal.ReadInt32(buffer, 8 + 2 * IntPtr.Size);
            var count = Marshal.ReadInt32(buffer, 12 + 2 * IntPtr.Size);
            if (reportLength <= 0 || count <= 0 || (long)reportLength * count > size - (16 + 2 * IntPtr.Size))
                return 0;
            for (var i = 0; i < count; i++)
            {
                var reportBytes = new byte[reportLength];
                Marshal.Copy(buffer + 16 + 2 * IntPtr.Size + i * reportLength, reportBytes, 0, reportLength);
                try { Decode(device, reportBytes); }
                catch (Exception ex) { ErrorOccurred?.Invoke(this, $"Wheel report could not be read: {ex.Message}"); }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return 0;
    }

    internal ParsedDevice? ResolveAndSelect(nint handle, Func<nint, ParsedDevice?> resolve)
    {
        if (!_devices.TryGetValue(handle, out var device))
        {
            _devices[handle] = device = resolve(handle);
        }
        if (device is null) return null;
        // Selection must run for cached handles as well as newly resolved ones.
        if (device.IsPedal) _activePedalPath ??= device.Path;
        else if (device.IsPrimaryWheel && device.InstanceId == _selectedInstanceId && _activeDevicePath is null)
        {
            _activeDevicePath = device.Path;
            ActiveWheelId = device.InstanceId;
            RefreshControls();
            ActiveWheelChanged?.Invoke(this, ActiveWheelId);
        }
        if (!AcceptsDevice(device.InstanceId, device.IsPedal, _selectedInstanceId, _activeDevicePath is not null)) return null;
        if (device.IsPedal && _activePedalPath != device.Path) return null;
        return device;
    }


    private ParsedDevice? ResolveDevice(nint handle)
    {
        uint size = 32;
        var info = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.WriteInt32(info, 32);
            if (GetRawInputDeviceInfo(handle, RidiDeviceInfo, info, ref size) == uint.MaxValue ||
                Marshal.ReadInt32(info, 4) != 2 || Marshal.ReadInt32(info, 8) != 0x046d) return null;
            var productId = Marshal.ReadInt32(info, 12);
            uint nameLength = 0;
            GetRawInputDeviceInfo(handle, 0x20000007, nint.Zero, ref nameLength);
            string? rawPath = null;
            if (nameLength is > 0 and < 4096)
            {
                var name = Marshal.AllocHGlobal((int)nameLength * 2);
                try
                {
                    if (GetRawInputDeviceInfo(handle, 0x20000007, name, ref nameLength) != uint.MaxValue)
                        rawPath = Marshal.PtrToStringUni(name);
                }
                finally { Marshal.FreeHGlobal(name); }
            }
            var allCandidates = DeviceList.Local.GetHidDevices(0x046d, productId).ToArray();
            var candidates = allCandidates.Where(hid => DevicePathsMatch(hid.DevicePath, rawPath)).ToArray();
            if (candidates.Length == 0 && allCandidates.Length == 1) candidates = allCandidates;
            foreach (var hid in candidates)
            {
                try
                {
                    var descriptor = hid.GetReportDescriptor();
                    if (!descriptor.DeviceItems.Any(item => item.InputReports.Any())) continue;
                    var definition = _definitions.FirstOrDefault(wheel => wheel.ProductIds.Contains(productId));
                    string productName;
                    try { productName = hid.GetProductName(); }
                    catch { productName = string.Empty; }
                    if (definition is null && !productName.Contains("wheel", StringComparison.OrdinalIgnoreCase) &&
                        !productName.Contains("racing", StringComparison.OrdinalIgnoreCase) &&
                        !productName.Contains("pedal", StringComparison.OrdinalIgnoreCase)) continue;
                    var wheelId = definition?.Id ?? $"logitech-{productId:x4}";
                    var controls = definition?.InputControls ?? [];
                    var isPedal = definition?.IsPedalSet == true ||
                                  productName.Contains("pedal", StringComparison.OrdinalIgnoreCase);
                    var isPrimaryWheel = !isPedal && descriptor.InputReports.SelectMany(report => report.DataItems)
                        .Any(item => item.Usages.GetAllValues().Any(usage => (uint)usage == 0x00010030));
                    return new ParsedDevice(hid.DevicePath, wheelId, NativeWheelIdentity.GetInstanceId(hid.DevicePath),
                        descriptor, controls, isPedal, isPrimaryWheel);
                }
                catch { /* A vendor-only interface may have no readable input descriptor. */ }
            }
        }
        finally { Marshal.FreeHGlobal(info); }
        return null;
    }

    internal static bool AcceptsDevice(string instanceId, bool isPedal, string? selected, bool hasWheel) =>
        selected is not null && (isPedal ? hasWheel : instanceId.Equals(selected, StringComparison.OrdinalIgnoreCase));

    private void RefreshControls()
    {
        if (_discoverControls is not null) { AvailableControls = _discoverControls(_selectedInstanceId); return; }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_selectedInstanceId is not null)
        {
            try
            {
                foreach (var hid in DeviceList.Local.GetHidDevices(0x046d))
                {
                    var definition = _definitions.FirstOrDefault(w => w.ProductIds.Contains(hid.ProductID));
                    string productName;
                    try { productName = hid.GetProductName(); } catch { productName = string.Empty; }
                    var pedal = definition?.IsPedalSet == true || productName.Contains("pedal", StringComparison.OrdinalIgnoreCase);
                    if (!pedal && NativeWheelIdentity.GetInstanceId(hid.DevicePath) != _selectedInstanceId) continue;
                    try
                    {
                        foreach (var usage in hid.GetReportDescriptor().InputReports.SelectMany(r => r.DataItems)
                                     .SelectMany(i => i.Usages.GetAllValues()))
                            names.Add(ResolveName((uint)usage, definition?.InputControls ?? [], pedal));
                    }
                    catch { }
                }
            }
            catch { return; }
        }
        AvailableControls = names.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void Decode(ParsedDevice device, byte[] bytes)
    {
        if (bytes.Length == 0) return;
        var report = device.Descriptor.InputReports.FirstOrDefault(candidate => candidate.ReportID == bytes[0]);
        if (report is null) return;
        var seenButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var usage in report.DataItems.SelectMany(item => item.Usages.GetAllValues())
                     .Where(usage => (uint)usage >> 16 == 9))
            seenButtons.Add(ResolveName((uint)usage, device.Controls, device.IsPedal));
        var activeButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        report.Read(bytes, 0, value =>
        {
            if (!value.IsValid || value.IsNull) return;
            foreach (var usage in value.Usages)
            {
                var name = ResolveName((uint)usage, device.Controls, device.IsPedal);
                if (value.DataItem.IsBoolean || (uint)usage >> 16 == 9)
                {
                    if (value.GetLogicalValue() != 0) activeButtons.Add(name);
                }
                else
                {
                    var minimum = value.DataItem.LogicalMinimum;
                    var maximum = value.DataItem.LogicalMaximum;
                    if (maximum <= minimum) continue;
                    var fraction = Math.Clamp((value.GetLogicalValue() - minimum) / (double)(maximum - minimum), 0, 1);
                    var declared = device.Controls.FirstOrDefault(control => control.Name == name);
                    if (declared?.Inverted == true) fraction = 1 - fraction;
                    InputReceived?.Invoke(this, new WheelInputSample(name,
                        (declared?.Centered == true || name == "Steering" ? fraction * 200 - 100 : fraction * 100), false));
                }
            }
        });
        foreach (var button in seenButtons)
            InputReceived?.Invoke(this, new WheelInputSample(button, activeButtons.Contains(button) ? 100 : 0, true));
    }

    private static string ResolveName(uint usage, IReadOnlyList<WheelInputControl> controls, bool isPedal)
    {
        var declared = controls.FirstOrDefault(control =>
            usage == ((uint)control.UsagePage << 16 | (uint)control.Usage));
        if (declared is not null) return declared.Name;
        var page = usage >> 16;
        var id = usage & 0xffff;
        if (page == 9) return $"Button{id}";
        if (page == 1 && isPedal) return id switch
        {
            0x30 => "Accelerator", 0x31 => "Brake", 0x32 => "Clutch", _ => $"Pedal axis {id:X2}"
        };
        if (page == 1) return id switch
        {
            0x30 => "Steering", 0x31 => "Accelerator", 0x32 => "Brake", 0x35 => "Clutch",
            _ => $"Axis {id:X2}"
        };
        return $"HID {page:X2}:{id:X2}";
    }

    private static bool DevicePathsMatch(string left, string? right)
    {
        if (right is null) return false;
        static string Normalize(string path) => path.Replace(@"\??\", @"\\?\", StringComparison.OrdinalIgnoreCase);
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
}
