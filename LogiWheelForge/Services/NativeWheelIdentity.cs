using System.Runtime.InteropServices;

namespace LogiWheelForge.Services;

/// <summary>Windows device containers group the HID interfaces of one physical USB device.</summary>
internal static class NativeWheelIdentity
{
    private static readonly Guid HidClass = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly PropertyKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    private static IReadOnlyDictionary<string, string> _identities = new Dictionary<string, string>();

    public static string NormalizePath(string path) => path.Replace(@"\??\", @"\\?\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
    public static string GetInstanceId(string path) => _identities.TryGetValue(NormalizePath(path), out var id)
        ? id : "hid:" + NormalizePath(path);

    public static void Refresh()
    {
        if (!OperatingSystem.IsWindows()) return;
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hidClass = HidClass;
        var set = SetupDiGetClassDevs(ref hidClass, null, 0, 0x12);
        if (set == -1) return;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref hidClass, index, ref data)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out var required, 0);
                if (required is < 8 or > 65536) continue;
                var detail = Marshal.AllocHGlobal((int)required);
                var devInfo = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceInfo>());
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    Marshal.StructureToPtr(new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() }, devInfo, false);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, devInfo)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    var key = ContainerId;
                    var bytes = new byte[16];
                    if (path is not null && SetupDiGetDeviceProperty(set, devInfo, ref key, out var type,
                            bytes, 16, out _, 0) && type == 0x0d && new Guid(bytes) != Guid.Empty)
                        identities[NormalizePath(path)] = "container:" + new Guid(bytes).ToString("D");
                }
                finally { Marshal.FreeHGlobal(detail); Marshal.FreeHGlobal(devInfo); }
            }
            _identities = identities;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public int Size; public Guid Class; public uint Flags; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo { public int Size; public Guid Class; public uint DevInst; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid format, uint id) { public readonly Guid Format = format; public readonly uint Id = id; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")]
    private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint window, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint info);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW")]
    private static extern bool SetupDiGetDeviceProperty(nint set, nint info, ref PropertyKey key, out uint type, byte[] value, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(nint set);
}
