using System.Globalization;

namespace LogiWheelForge.ViewModels;

/// <summary>Retains invalid text in the draft so navigation cannot silently discard it or save stale numbers.</summary>
internal sealed class DraftNumberFields
{
    private readonly Dictionary<string, string> _text = [];
    private readonly HashSet<string> _invalid = [];
    private bool _typing;
    public string Error => _invalid.Count == 0 ? string.Empty : "Enter a valid number for " + string.Join(", ", _invalid) + ".";
    public string Get(string name, double value) => _text.GetValueOrDefault(name) ?? value.ToString(CultureInfo.CurrentCulture);
    public void Set(string name, string value, Action<double> apply, Action notify)
    {
        _text[name] = value;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && double.IsFinite(number))
        {
            _invalid.Remove(name);
            _typing = true;
            try { apply(number); } finally { _typing = false; }
        }
        else { _invalid.Add(name); notify(); }
    }
    public void Clear(string name) { if (!_typing) { _text.Remove(name); _invalid.Remove(name); } }
    public bool IsInvalid(string name) => _invalid.Contains(name);
    public void Validate() { if (_invalid.Count > 0) throw new ArgumentException(Error); }
}
