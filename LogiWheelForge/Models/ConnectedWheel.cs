namespace LogiWheelForge.Models;

/// <summary>A physical wheel, which may expose several HID collections. Definition IDs describe models only.</summary>
public sealed record ConnectedWheel(string InstanceId, string DisplayName, string DefinitionId,
    IReadOnlyList<string> HidInterfaces, string ResistanceDeviceIdentity)
{
    public string PickerLabel { get; init; } = DisplayName;
    public override string ToString() => PickerLabel;
}

public sealed record WheelDeviceSnapshot(IReadOnlyList<ConnectedWheel> Connected, ConnectedWheel? Active);

public sealed record WheelInterface(string InstanceId, string Path, string DisplayName, string DefinitionId,
    bool IsWheel, bool IsPedal);
