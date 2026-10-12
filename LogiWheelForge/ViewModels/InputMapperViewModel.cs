using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using LogiWheelForge.Commands;
using LogiWheelForge.Controls;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.Views;

namespace LogiWheelForge.ViewModels;

public sealed class MapperRuleDraft : ObservableObject, IDataErrorInfo
{
    private MapperRule _value;
    public MapperRuleDraft(MapperRule rule) => _value = MapperOutputValidation.Normalize(rule);
    public string Id => _value.Id;
    public string Control { get => _value.Control; set => Change(_value with { Control = value }); }
    public MapperTriggerKind Trigger { get => _value.Trigger; set => Change(_value with { Trigger = value }); }
    public MapperStepMode StepMode { get => _value.StepMode; set => Change(_value with { StepMode = value }); }
    public double StepPercent { get => _value.StepPercent; set => Change(_value with { StepPercent = value }); }
    public double MinPercent { get => _value.MinPercent; set => Change(_value with { MinPercent = value }); }
    public double MaxPercent { get => _value.MaxPercent; set => Change(_value with { MaxPercent = value }); }
    public double HysteresisPercent { get => _value.HysteresisPercent; set => Change(_value with { HysteresisPercent = value }); }
    public double DeadZonePercent { get => _value.DeadZonePercent; set => Change(_value with { DeadZonePercent = value }); }
    public double CurveExponent { get => _value.CurveExponent; set => Change(_value with { CurveExponent = value }); }
    public int Direction { get => _value.Direction; set => Change(_value with { Direction = value }); }
    public MapperActionKind Action { get => _value.Action; set => Change(_value with { Action = value }); }
    public MapperActionMode Mode { get => _value.Mode; set => Change(_value with { Mode = value }); }
    public string Output { get => _value.Output; set => Change(_value with { Output = value }); }
    public int DurationMs { get => _value.DurationMs; set => Change(_value with { DurationMs = value }); }
    public double OutputScale { get => _value.OutputScale; set => Change(_value with { OutputScale = value }); }
    public double ForceStrength { get => _value.ForceStrength; set => Change(_value with { ForceStrength = value }); }
    public double TargetPercent { get => _value.TargetPercent; set => Change(_value with { TargetPercent = value }); }
    public MapperForceRelease ForceRelease { get => _value.ForceRelease; set => Change(_value with { ForceRelease = value }); }
    public bool IsStepTrigger => Trigger == MapperTriggerKind.AxisStep;
    public bool IsRangeTrigger => Trigger == MapperTriggerKind.AxisRange;
    public bool IsAxisTrigger => Trigger != MapperTriggerKind.Button;
    public bool IsKeyOrButtonAction => Action is MapperActionKind.Key or MapperActionKind.MouseButton;
    public bool IsMovementAction => Action is MapperActionKind.MouseMove or MapperActionKind.MouseScroll;
    public bool IsHoldTargetAction => Action == MapperActionKind.HoldTarget;
    public bool IsForceAction => Action is MapperActionKind.HoldTarget or MapperActionKind.ReleaseTarget;
    public bool IsTapAction => IsKeyOrButtonAction && Mode == MapperActionMode.Tap;
    public bool UsesOutput => IsKeyOrButtonAction || Action == MapperActionKind.MouseMove;
    public bool HasFineTuning => IsAxisTrigger || IsTapAction;
    public string TriggerLabel => Label(Trigger);
    public string ActionLabel => Label(Action);
    public string OutputSummary => UsesOutput ? Output : Action == MapperActionKind.MouseScroll
        ? $"Scale {OutputScale:0.##}" : IsHoldTargetAction ? $"Position {TargetPercent:0.#}%" : "Release hold";
    public string OutputLabel => Action switch
    {
        MapperActionKind.MouseButton => "Mouse button",
        MapperActionKind.MouseMove => "Movement axis",
        _ => "Key or shortcut"
    };
    public string OutputHint => Action switch
    {
        MapperActionKind.MouseButton => "Choose Left, Right, Middle, X1 or X2.",
        MapperActionKind.MouseMove => "Horizontal moves left/right; Vertical moves up/down.",
        _ => "Type a key name or shortcut, such as Space or LeftCtrl+A."
    };
    public IReadOnlyList<string> OutputChoices => Action switch
    {
        MapperActionKind.MouseButton => ["Left", "Right", "Middle", "X1", "X2"],
        MapperActionKind.MouseMove => ["Horizontal", "Vertical"],
        _ => ["W", "A", "S", "D", "Space", "Enter", "Escape", "Left", "Right", "Up", "Down"]
    };
    public string ValidationMessage => _value.TryValidate(out var error) ? string.Empty : error;
    private static string Label(object value) => (string)new MapperChoiceLabelConverter().Convert(
        value, typeof(string), null!, System.Globalization.CultureInfo.CurrentCulture);
    public string Summary => $"{Control} · {TriggerLabel} → {ActionLabel} {OutputSummary}";
    public string Error => ValidationMessage;
    public string this[string columnName] => columnName switch
    {
        nameof(Output) when Action == MapperActionKind.Key && !MapperOutputValidation.IsValidShortcut(Output) => "Enter a valid key or shortcut.",
        nameof(Output) when Action == MapperActionKind.MouseButton && !MapperOutputValidation.IsMouseButton(Output) => "Choose a supported mouse button.",
        nameof(Output) when Action == MapperActionKind.MouseMove && MapperOutputValidation.NormalizeMouseAxis(Output) is not ("Horizontal" or "Vertical") => "Choose Horizontal or Vertical.",
        nameof(StepPercent) when !double.IsFinite(StepPercent) || StepPercent is <= 0 or > 100 => "Use a step greater than 0 and at most 100%.",
        nameof(MinPercent) or nameof(MaxPercent) when !double.IsFinite(MinPercent) || !double.IsFinite(MaxPercent) || MinPercent < -100 || MaxPercent > 100 || MinPercent >= MaxPercent => "Use an ordered range between −100 and 100%.",
        nameof(DurationMs) when DurationMs is < 10 or > 5000 => "Use 10–5000 milliseconds.",
        _ => string.Empty
    };
    public MapperRule Build() => MapperOutputValidation.Normalize(_value);
    private void Change(MapperRule value)
    {
        _value = value;
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(Summary));
    }
}

public sealed class MapperProfileDraft : ObservableObject
{
    private string _name;
    private bool _enabled;
    private bool _resistanceEnabled, _detentsEnabled;
    private double _centerStrength, _dampingStrength, _detentSpacingPercent, _detentHysteresisPercent, _detentStrength;

    public MapperProfileDraft(InputMapperProfile profile)
    {
        Id = profile.Id; _name = profile.Name; _enabled = profile.Enabled;
        ProcessPaths = new ObservableCollection<string>(profile.ProcessPaths);
        Rules = new ObservableCollection<MapperRuleDraft>(profile.Rules.Select(rule => new MapperRuleDraft(rule)));
        _resistanceEnabled = profile.Resistance.Enabled;
        _centerStrength = profile.Resistance.CenterStrength;
        _dampingStrength = profile.Resistance.DampingStrength;
        _detentsEnabled = profile.Resistance.DetentsEnabled;
        _detentSpacingPercent = profile.Resistance.DetentSpacingPercent;
        _detentHysteresisPercent = profile.Resistance.DetentHysteresisPercent;
        _detentStrength = profile.Resistance.DetentStrength;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Name) or nameof(Enabled) or nameof(ResistanceEnabled) or nameof(CenterStrength) or
                nameof(DampingStrength) or nameof(DetentsEnabled) or nameof(DetentSpacingPercent) or nameof(DetentHysteresisPercent) or nameof(DetentStrength))
                NotifyChanged();
        };
        ProcessPaths.CollectionChanged += (_, _) => NotifyChanged();
        foreach (var rule in Rules) rule.PropertyChanged += OnRuleChanged;
        Rules.CollectionChanged += OnRulesChanged;
    }
    public event EventHandler? Changed;
    public string Id { get; }
    public string Name { get => _name; set => SetField(ref _name, value); }
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }
    public ObservableCollection<string> ProcessPaths { get; }
    public ObservableCollection<MapperRuleDraft> Rules { get; }
    public bool ResistanceEnabled { get => _resistanceEnabled; set => SetField(ref _resistanceEnabled, value); }
    public double CenterStrength { get => _centerStrength; set => SetField(ref _centerStrength, value); }
    public double DampingStrength { get => _dampingStrength; set => SetField(ref _dampingStrength, value); }
    public bool DetentsEnabled { get => _detentsEnabled; set => SetField(ref _detentsEnabled, value); }
    public double DetentSpacingPercent { get => _detentSpacingPercent; set => SetField(ref _detentSpacingPercent, value); }
    public double DetentHysteresisPercent { get => _detentHysteresisPercent; set => SetField(ref _detentHysteresisPercent, value); }
    public double DetentStrength { get => _detentStrength; set => SetField(ref _detentStrength, value); }
    public InputMapperProfile Build() => new()
    {
        Id = Id, Name = Name?.Trim() ?? string.Empty, Enabled = Enabled,
        ProcessPaths = ProcessPaths.Select(path => Path.GetFullPath(path.Trim())).ToArray(),
        Rules = Rules.Select(rule => rule.Build()).ToArray(),
        Resistance = new MapperResistance
        {
            Enabled = ResistanceEnabled, CenterStrength = CenterStrength, DampingStrength = DampingStrength,
            DetentsEnabled = DetentsEnabled, DetentSpacingPercent = DetentSpacingPercent,
            DetentHysteresisPercent = DetentHysteresisPercent,
            DetentStrength = DetentStrength
        }
    };

    public string ApplicationsSummary => ProcessPaths.Count == 0 ? "No applications linked" : string.Join(" · ", ProcessPaths.Select(Path.GetFileName));
    public string EnabledLabel => Enabled ? "Enabled" : "Disabled";
    public string NameError => string.IsNullOrWhiteSpace(Name) || Name.Length > 64 ? "Enter a profile name of up to 64 characters." : string.Empty;
    public string ApplicationsError { get; private set; } = string.Empty;
    public string ResistanceError => BuildResistance().TryValidate() ? string.Empty : "Check the resistance strengths, spacing and hysteresis.";
    public string SetupWarning => !string.IsNullOrEmpty(NameError) ? NameError : !string.IsNullOrEmpty(ApplicationsError) ? ApplicationsError :
        !string.IsNullOrEmpty(ResistanceError) ? ResistanceError : Rules.Any(r => r.ValidationMessage.Length > 0) ? "A mapping needs attention" :
        ProcessPaths.Count == 0 ? "Link an application to go live" : Rules.Count == 0 ? "Add a mapping to get started" : string.Empty;
    private MapperResistance BuildResistance() => new() { Enabled = ResistanceEnabled, CenterStrength = CenterStrength,
        DampingStrength = DampingStrength, DetentsEnabled = DetentsEnabled, DetentSpacingPercent = DetentSpacingPercent,
        DetentHysteresisPercent = DetentHysteresisPercent, DetentStrength = DetentStrength };
    public void SetApplicationsError(string error) { ApplicationsError = error; OnPropertyChanged(nameof(ApplicationsError)); OnPropertyChanged(nameof(SetupWarning)); }
    private void NotifyChanged()
    {
        foreach (var name in new[] { nameof(ApplicationsSummary), nameof(EnabledLabel), nameof(NameError), nameof(ResistanceError), nameof(SetupWarning) }) OnPropertyChanged(name);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e) => NotifyChanged();
    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (MapperRuleDraft rule in e.OldItems) rule.PropertyChanged -= OnRuleChanged;
        if (e.NewItems is not null)
            foreach (MapperRuleDraft rule in e.NewItems) rule.PropertyChanged += OnRuleChanged;
        NotifyChanged();
    }
}

public sealed class InputMapperViewModel : ObservableObject, IDisposable
{
    private readonly InputMapperService _service;
    private readonly RevisionedAutosave<IReadOnlyList<InputMapperProfile>> _autosave;
    private readonly IDisposable? _presentation;
    private readonly LatestSnapshot<InputMapperSnapshot> _latest = new();
    private MapperProfileDraft? _selectedProfile;
    private MapperRuleDraft? _selectedRule;
    private string _status = "Input Mapper is stopped", _inputPreview = "Move a wheel control to preview it";
    private string _manualProcessPath = string.Empty;
    private string? _selectedProcessPath;
    private bool _isRunning;
    private bool _hasUnsavedChanges;
    private string _pageTitle = "Input Mapper";
    private int _editorTabIndex;

    public InputMapperViewModel(InputMapperService service, PresentationUpdateCoordinator? presentation = null, Func<bool>? mounted = null)
    {
        _service = service;
        _autosave = new(() => Profiles.Select(p => p.Build()).ToArray(), InputMapperProfileStore.Validate,
            profiles => _service.SaveProfilesAsync(profiles));
        _autosave.Changed += OnSaveChanged;
        _presentation = presentation?.Register(RefreshPresentation, mounted);
        OpenProfileCommand = new ParameterCommand<MapperProfileDraft>(profile => { SelectedProfile = profile; EditorTabIndex = 0; });
        BackCommand = new RelayCommand(() => SelectedProfile = null);
        _service.SnapshotChanged += OnSnapshot;
        _service.InputPreview += OnInputPreview;
        AddProfileCommand = new RelayCommand(AddProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => SelectedProfile is not null);
        AddRuleCommand = new RelayCommand(AddRule, () => SelectedProfile is not null);
        DeleteRuleCommand = new RelayCommand(DeleteRule, () => SelectedRule is not null);
        AddProcessCommand = new RelayCommand(AddManualProcess, () => SelectedProfile is not null && !string.IsNullOrWhiteSpace(ManualProcessPath));
        RemoveProcessCommand = new RelayCommand(RemoveProcess, () => SelectedProcessPath is not null);
        PickWindowCommand = new RelayCommand(PickWindow, () => SelectedProfile is not null);
        SaveCommand = new AsyncRelayCommand(SaveChangesAsync, onError: ex => Status = $"Could not save: {ex.Message}");
        ToggleCommand = new RelayCommand(() =>
        {
            try { if (_service.IsRunning) _service.Stop(); else _service.Start(); }
            catch (Exception ex) { Status = $"Could not start mapper: {ex.Message}"; }
        });
    }

    public ObservableCollection<MapperProfileDraft> Profiles { get; } = [];
    public string PageTitle { get => _pageTitle; set => SetField(ref _pageTitle, value); }
    public ObservableCollection<string> Controls { get; } = ["Steering", "Accelerator", "Brake", "Clutch"];
    public int EditorTabIndex { get => _editorTabIndex; set => SetField(ref _editorTabIndex, value); }
    public IReadOnlyList<MapperTriggerKind> Triggers { get; } = Enum.GetValues<MapperTriggerKind>();
    public IReadOnlyList<MapperStepMode> StepModes { get; } = Enum.GetValues<MapperStepMode>();
    public IReadOnlyList<MapperActionKind> Actions { get; } = Enum.GetValues<MapperActionKind>();
    public IReadOnlyList<MapperActionMode> Modes { get; } = Enum.GetValues<MapperActionMode>();
    public IReadOnlyList<MapperForceRelease> ForceReleases { get; } = Enum.GetValues<MapperForceRelease>();
    public IReadOnlyList<int> Directions { get; } = [-1, 1];
    public ICommand OpenProfileCommand { get; }
    public ICommand BackCommand { get; }
    public bool IsEditorOpen => SelectedProfile is not null;
    public string? SaveError => _autosave.Error;
    public ObservableCollection<MappingDiagnosticRow> ActivityRows { get; } = [];
    public string ForceStatus { get; private set; } = "Resistance is off";
    public string ControlAvailability => SelectedRule is null ? string.Empty : _service.AvailableControls.Contains(SelectedRule.Control) ? string.Empty : $"{SelectedRule.Control} is unavailable. Connect the selected wheel or choose an available control.";
    public ICommand AddProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand AddProcessCommand { get; }
    public ICommand RemoveProcessCommand { get; }
    public ICommand PickWindowCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ToggleCommand { get; }
    public MapperProfileDraft? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetField(ref _selectedProfile, value)) return;
            SelectedRule = value?.Rules.FirstOrDefault();
            SelectedProcessPath = null;
            ManualProcessPath = string.Empty;
            OnPropertyChanged(nameof(IsEditorOpen));
            UpdatePreview();
            RefreshCommands();
        }
    }
    public MapperRuleDraft? SelectedRule { get => _selectedRule; set { if (SetField(ref _selectedRule, value)) { OnPropertyChanged(nameof(ControlAvailability)); RefreshCommands(); } } }
    public string? SelectedProcessPath { get => _selectedProcessPath; set { if (SetField(ref _selectedProcessPath, value)) RefreshCommands(); } }
    public string ManualProcessPath { get => _manualProcessPath; set { if (SetField(ref _manualProcessPath, value)) RefreshCommands(); } }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string InputPreview { get => _inputPreview; private set => SetField(ref _inputPreview, value); }
    public string ToggleText => _isRunning ? "Stop mapper" : "Start mapper";
    public bool IsRunning => _isRunning;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetField(ref _hasUnsavedChanges, value);
    }
    public string EditStatus => _autosave.Status;

    public void Initialize()
    {
        Profiles.Clear();
        foreach (var profile in _service.Profiles)
        {
            var draft = new MapperProfileDraft(profile);
            draft.Changed += OnDraftChanged;
            Profiles.Add(draft);
        }
        SelectedProfile = null;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(EditStatus));
    }

    public void Dispose()
    {
        _autosave.Dispose(); _presentation?.Dispose();
        foreach (var profile in Profiles) profile.Changed -= OnDraftChanged;
        _service.SetPreviewProfile(null);
        _service.SnapshotChanged -= OnSnapshot;
        _service.InputPreview -= OnInputPreview;
    }

    private void AddProfile()
    {
        var profile = new InputMapperProfile
        {
            Name = "New mapping",
            Rules =
            [
                new MapperRule { Control = "Steering", Direction = 1, Output = "D" },
                new MapperRule { Control = "Steering", Direction = -1, Output = "A" },
                new MapperRule { Control = "Accelerator", Trigger = MapperTriggerKind.AxisRange,
                    Mode = MapperActionMode.Hold, Output = "W", MinPercent = 10 },
                new MapperRule { Control = "Brake", Trigger = MapperTriggerKind.AxisRange,
                    Mode = MapperActionMode.Hold, Output = "S", MinPercent = 10 }
            ]
        };
        var draft = new MapperProfileDraft(profile);
        draft.Changed += OnDraftChanged;
        Profiles.Add(draft); SelectedProfile = draft;
        EditorTabIndex = 1;
        MarkUnsaved();
        Status = "Link an application, then review the starter mappings";
    }
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        SelectedProfile.Changed -= OnDraftChanged;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = null;
        MarkUnsaved();
    }
    private void AddRule()
    {
        if (SelectedProfile is null) return;
        var rule = new MapperRuleDraft(new MapperRule());
        SelectedProfile.Rules.Add(rule); SelectedRule = rule;
        EditorTabIndex = 0;
    }
    private void DeleteRule()
    {
        if (SelectedProfile is null || SelectedRule is null) return;
        SelectedProfile.Rules.Remove(SelectedRule);
        SelectedRule = SelectedProfile.Rules.FirstOrDefault();
    }
    private void AddManualProcess()
    {
        if (SelectedProfile is null) return;
        var path = ManualProcessPath.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        { Status = "Enter a full .exe path"; return; }
        AddProcess(path);
        ManualProcessPath = string.Empty;
    }
    private void AddProcess(string path)
    {
        if (SelectedProfile is null) return;
        if (SelectedProfile.ProcessPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        { Status = "This executable is already in the profile"; return; }
        SelectedProfile.ProcessPaths.Add(path);
    }
    private void RemoveProcess()
    {
        if (SelectedProfile is null || SelectedProcessPath is null) return;
        SelectedProfile.ProcessPaths.Remove(SelectedProcessPath);
        SelectedProcessPath = null;
    }
    private void PickWindow()
    {
        var path = ProcessWindowPicker.Pick();
        if (path is null) { Status = "No executable was selected"; return; }
        AddProcess(path);
    }
    public Task SaveChangesAsync() => _autosave.FlushAsync();
    private void OnSaveChanged(object? sender, EventArgs e)
    {
        HasUnsavedChanges = _autosave.IsDirty;
        OnPropertyChanged(nameof(EditStatus)); OnPropertyChanged(nameof(SaveError));
    }
    private void UpdatePreview()
    {
        try
        {
            var profile = SelectedProfile?.Build();
            _service.SetPreviewProfile(profile is not null && profile.TryValidate(out _) ? profile : null);
        }
        catch (ArgumentException) { _service.SetPreviewProfile(null); }
    }
    private void ValidateDrafts()
    {
        foreach (var profile in Profiles)
        {
            var conflict = profile.ProcessPaths.FirstOrDefault(path => profile.Enabled && Profiles.Any(other =>
                other != profile && other.Enabled && other.ProcessPaths.Contains(path, StringComparer.OrdinalIgnoreCase)));
            profile.SetApplicationsError(conflict is null ? string.Empty : $"This application is already linked to another enabled profile: {Path.GetFileName(conflict)}");
        }
    }
    private void OnDraftChanged(object? sender, EventArgs e) => MarkUnsaved();
    private void RefreshCommands()
    {
        foreach (var command in new[] { DeleteProfileCommand, AddRuleCommand, DeleteRuleCommand,
                     AddProcessCommand, RemoveProcessCommand, PickWindowCommand })
            (command as RelayCommand)?.RaiseCanExecuteChanged();
    }
    private void MarkUnsaved()
    {
        ValidateDrafts();
        UpdatePreview();
        _autosave.Edit();
        OnPropertyChanged(nameof(ControlAvailability));
    }
    private void OnSnapshot(object? sender, InputMapperSnapshot snapshot)
    {
        _isRunning = snapshot.IsRunning; // Tray commands stay current while presentation is hidden.
        _latest.Set(snapshot);
        if (_presentation is null) RefreshPresentation();
    }
    private void OnInputPreview(object? sender, WheelInputSample sample)
    {
        if (_presentation is null) RefreshPresentation();
    }
    private void RefreshPresentation()
    {
        if (_latest.Value is { } snapshot)
        {
            Status = snapshot.ActiveProfile is null ? "Live · " + snapshot.Status :
                $"Live · {snapshot.ActiveProfile} · {snapshot.ActiveApplication} · {snapshot.Status}";
            ForceStatus = snapshot.ForceStatus;
            OnPropertyChanged(nameof(ForceStatus));
        }
        OnPropertyChanged(nameof(ToggleText)); OnPropertyChanged(nameof(IsRunning));
        var diagnostics = _service.Diagnostics.Snapshot();
        InputPreview = diagnostics.Inputs.Count == 0 ? "Preview · move a wheel control to try this profile" :
            "Preview · " + string.Join(" · ", diagnostics.Inputs.Where(s => !s.IsButton || s.Percent > 0).Select(s => $"{s.Control}: {s.Percent:0.#}%"));
        var available = _service.AvailableControls.Concat(SelectedProfile?.Rules.Select(r => r.Control) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!Controls.SequenceEqual(available)) { Controls.Clear(); foreach (var control in available) Controls.Add(control); }
        OnPropertyChanged(nameof(ControlAvailability));
        MappingDiagnosticRow.Update(ActivityRows, SelectedProfile?.Rules.Select(r => r.Build()).ToArray() ?? [],
            diagnostics.Preview.Where(a => a.ProfileId == SelectedProfile?.Id), "Preview");
    }
}
