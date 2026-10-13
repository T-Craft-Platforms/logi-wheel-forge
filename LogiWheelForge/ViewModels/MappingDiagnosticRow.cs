using System.Collections.ObjectModel;
using LogiWheelForge.Models;
using LogiWheelForge.Services;

namespace LogiWheelForge.ViewModels;

public sealed class MappingDiagnosticRow : ObservableObject
{
    private bool _isActive;
    private string _state = "Waiting", _summary = string.Empty;
    public MappingDiagnosticRow(string id) => Id = id;
    public string Id { get; }
    public string Summary { get => _summary; private set => SetField(ref _summary, value); }
    public string State { get => _state; private set => SetField(ref _state, value); }
    public bool IsActive { get => _isActive; private set => SetField(ref _isActive, value); }
    public static void Update(ObservableCollection<MappingDiagnosticRow> rows, IReadOnlyList<MapperRule> rules,
        IEnumerable<MappingActivity> activity, string mode)
    {
        var states = activity.ToDictionary(a => a.RuleId);
        foreach (var removed in rows.Where(r => rules.All(rule => rule.Id != r.Id)).ToArray()) rows.Remove(removed);
        foreach (var rule in rules)
        {
            var row = rows.FirstOrDefault(r => r.Id == rule.Id);
            if (row is null) rows.Add(row = new(rule.Id));
            row.Summary = $"{rule.Control} → {rule.Trigger} → {rule.Action} {rule.Output}";
            if (states.TryGetValue(rule.Id, out var state))
            {
                row.IsActive = state.State is MappingOutputState.Held or MappingOutputState.Queued ||
                    state.State == MappingOutputState.Triggered && DateTimeOffset.UtcNow - state.At < TimeSpan.FromMilliseconds(300);
                row.State = $"{mode} · {state.State}" + (state.Error is null ? "" : " · " + state.Error);
            }
            else { row.IsActive = false; row.State = mode + " · Waiting"; }
        }
    }
}
