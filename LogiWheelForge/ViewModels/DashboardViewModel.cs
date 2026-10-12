using System.Collections.ObjectModel;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Application = System.Windows.Application;

namespace LogiWheelForge.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly LedIndicatorService _service;
    private readonly LatestSnapshot<AppSnapshot> _latest = new();
    private readonly LatestSnapshot<WheelDeviceSnapshot> _devices = new();
    private WheelDeviceSnapshot? _appliedDevices;
    private string? _ledDefinitionId;
    private readonly LatestSnapshot<InputMapperSnapshot> _mapperLatest = new();
    private readonly IDisposable? _presentation;
    private readonly Func<bool> _ledEnabled, _mapperEnabled;
    private readonly InputMapperService _mapper;
    private readonly WheelSelectionService _selection;
    private float _currentRpm, _maximumRpm;
    private string _currentVehicle = "No vehicle data", _wheelName = "No wheel detected", _telemetryFormat = "—";
    private string _mapperStatus = "Stopped";
    private bool _isFlashing;

    public DashboardViewModel(LedIndicatorService service, InputMapperService mapper,
        WheelSelectionService selection, PresentationUpdateCoordinator? presentation = null, Func<bool>? mounted = null, Func<bool>? ledEnabled = null, Func<bool>? mapperEnabled = null)
    {
        _service = service;
        _ledEnabled = ledEnabled ?? (() => true); _mapperEnabled = mapperEnabled ?? (() => true);
        Board = new(this);
        _presentation = presentation?.Register(RefreshPresentation, mounted);
        _mapper = mapper;
        _selection = selection;
        _service.SnapshotChanged += OnSnapshotChanged;
        _mapper.SnapshotChanged += OnMapperSnapshot;
        _selection.ActiveWheelChanged += OnActiveWheelChanged;
        _selection.DevicesChanged += OnDevicesChanged;
    }

    public DashboardBoardViewModel Board { get; }
    public Task InitializeLayoutAsync() => Board.InitializeAsync();
    public ObservableCollection<MappingDiagnosticRow> MappingRows { get; } = [];
    public IReadOnlyList<WheelInputSample> Inputs { get; private set; } = [];
    public string ResistanceAvailability => _mapper.ResistanceAvailability;
    public string ResistanceStatus { get; private set; } = "Resistance is off";
    public string ResistanceStrengths { get; private set; } = "Enable resistance in a mapping profile";
    public string ActiveApplication { get; private set; } = "Waiting for a linked application";
    public ObservableCollection<ConnectedWheel> ConnectedWheels { get; } = [];
    public bool HasMultipleWheels => ConnectedWheels.Count > 1;
    public ConnectedWheel? SelectedWheel { get => _selection.ActiveDevice; set { if (value is not null) _selection.SetPreferredWheel(value.InstanceId); } }
    public ObservableCollection<LedIndicatorViewModel> Leds { get; } = [];

    public string WheelName
    {
        get => _wheelName;
        private set => SetField(ref _wheelName, value);
    }
    public string MapperStatus { get => _mapperStatus; private set => SetField(ref _mapperStatus, value); }

    public string TelemetryFormat
    {
        get => _telemetryFormat;
        private set => SetField(ref _telemetryFormat, value);
    }

    public string CurrentVehicle
    {
        get => _currentVehicle;
        private set => SetField(ref _currentVehicle, value);
    }

    public float CurrentRpm
    {
        get => _currentRpm;
        private set
        {
            if (SetField(ref _currentRpm, value)) NotifyRpm();
        }
    }

    public float MaximumRpm
    {
        get => _maximumRpm;
        private set
        {
            if (SetField(ref _maximumRpm, value)) NotifyRpm();
        }
    }

    public bool IsFlashing
    {
        get => _isFlashing;
        private set
        {
            if (SetField(ref _isFlashing, value)) OnPropertyChanged(nameof(ShiftState));
        }
    }

    public string RpmDisplay => MaximumRpm > 0 ? $"{CurrentRpm:N0}" : "—";
    public string MaximumRpmDisplay => MaximumRpm > 0 ? $"/ {MaximumRpm:N0} RPM" : string.Empty;
    public string RpmPercentDisplay => MaximumRpm > 0 ? $"{CurrentRpm / MaximumRpm:P0}" : "—";
    public string ShiftState => IsFlashing ? "SHIFT NOW" : MaximumRpm > 0 ? "LIVE RPM" : "WAITING FOR DATA";

    public void Dispose()
    {
        Board.Dispose(); _presentation?.Dispose();
        _selection.DevicesChanged -= OnDevicesChanged;
        _service.SnapshotChanged -= OnSnapshotChanged;
        _mapper.SnapshotChanged -= OnMapperSnapshot;
        _selection.ActiveWheelChanged -= OnActiveWheelChanged;
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        _latest.Set(snapshot);
        if (_presentation is null) RefreshPresentation();
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        WheelName = _selection.ActiveDevice?.PickerLabel ?? "No wheel detected";
        TelemetryFormat = snapshot.TelemetryFormat;
        CurrentVehicle = snapshot.CurrentVehicle;
        CurrentRpm = snapshot.CurrentRpm;
        MaximumRpm = snapshot.MaximumRpm;
        IsFlashing = snapshot.IsFlashing;
        if (snapshot.PreviewWheel is not null) BuildLeds(snapshot.PreviewWheel, snapshot.IlluminatedLedCount);
        else
            foreach (var led in Leds)
                led.IsLit = false;
    }

    private void OnActiveWheelChanged(object? sender, string? wheelId)
    {
        _devices.Set(_selection.Snapshot);
        if (_presentation is null) RefreshPresentation();
    }
    private void OnDevicesChanged(object? sender, WheelDeviceSnapshot snapshot)
    {
        _devices.Set(snapshot);
        if (_presentation is null) RefreshPresentation();
    }
    private void OnMapperSnapshot(object? sender, InputMapperSnapshot snapshot)
    {
        _mapperLatest.Set(snapshot);
        if (_presentation is null) RefreshPresentation();
    }
    private void RefreshPresentation()
    {
        if (_devices.Value is { } devices && !ReferenceEquals(_appliedDevices, devices))
        {
            _appliedDevices = devices;
            ConnectedWheels.Clear(); foreach (var wheel in devices.Connected) ConnectedWheels.Add(wheel);
            WheelName = devices.Active?.PickerLabel ?? "No wheel detected";
            OnPropertyChanged(nameof(HasMultipleWheels)); OnPropertyChanged(nameof(SelectedWheel));
        }
        if (_ledEnabled() && Board.Widgets.Any(w => w.Kind is DashboardWidgetKind.Rpm or DashboardWidgetKind.Telemetry) && _latest.Value is { } led) ApplySnapshot(led);
        if (_mapperEnabled() && Board.Widgets.Any(w => w.Kind is DashboardWidgetKind.Mapper or DashboardWidgetKind.Inputs or DashboardWidgetKind.Mappings or DashboardWidgetKind.Resistance) && _mapperLatest.Value is { } mapper)
        {
            MapperStatus = mapper.ActiveProfile is null ? "Live · " + mapper.Status : $"Live · {mapper.ActiveProfile} · {mapper.Status}";
            ActiveApplication = mapper.ActiveApplication ?? "Waiting for a linked application";
            ResistanceStatus = mapper.ForceStatus;
            var profile = _mapper.Profiles.FirstOrDefault(p => p.Id == mapper.ActiveProfileId);
            ResistanceStrengths = profile is null ? "Choose a foreground application to see its wheel feel" :
                $"Center {profile.Resistance.CenterStrength:0}% · Damping {profile.Resistance.DampingStrength:0}% · Detents {profile.Resistance.DetentStrength:0}%";
            var diagnostics = _mapper.Diagnostics.Snapshot();
            Inputs = diagnostics.Inputs;
            MappingDiagnosticRow.Update(MappingRows, profile?.Rules ?? [], diagnostics.Live.Where(a => a.ProfileId == profile?.Id), "Live");
            foreach (var property in new[] { nameof(Inputs), nameof(ActiveApplication), nameof(ResistanceStatus), nameof(ResistanceStrengths), nameof(ResistanceAvailability) }) OnPropertyChanged(property);
        }
    }

    private void BuildLeds(WheelDefinition definition, int litGroups)
    {
        if (Leds.Count != definition.PhysicalLedCount || _ledDefinitionId != definition.Id)
        {
            _ledDefinitionId = definition.Id;
            Leds.Clear();
            for (var i = 0; i < definition.PhysicalLedCount; i++)
            {
                var group = definition.Direction == "outside-in"
                    ? Math.Min(i, definition.PhysicalLedCount - 1 - i) + 1
                    : Math.Min(i + 1, definition.ControlGroupCount);
                Leds.Add(new LedIndicatorViewModel(definition.Colors[i], group));
            }
        }

        foreach (var led in Leds) led.IsLit = led.Group <= litGroups;
    }

    private void NotifyRpm()
    {
        OnPropertyChanged(nameof(RpmDisplay));
        OnPropertyChanged(nameof(MaximumRpmDisplay));
        OnPropertyChanged(nameof(RpmPercentDisplay));
        OnPropertyChanged(nameof(ShiftState));
    }
}
