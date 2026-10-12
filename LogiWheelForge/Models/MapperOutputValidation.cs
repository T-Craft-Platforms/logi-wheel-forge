using System.Windows.Input;

namespace LogiWheelForge.Models;

public static class MapperOutputValidation
{
    public static string NormalizeMouseAxis(string? axis) => axis?.Trim().ToLowerInvariant() switch
    {
        "x" or "horizontal" => "Horizontal",
        "y" or "vertical" => "Vertical",
        _ => axis?.Trim() ?? string.Empty
    };
    public static bool IsValidShortcut(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries);
        return parts.Length is > 0 and <= 8 && parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() == parts.Length &&
               parts.All(part => part.Length > 0 && !char.IsDigit(part[0]) &&
                   Enum.TryParse<Key>(part, true, out var key) && Enum.IsDefined(key) && key != Key.None &&
                   KeyInterop.VirtualKeyFromKey(key) > 0);
    }
    public static bool IsMouseButton(string? value) => value?.Trim().ToLowerInvariant() is "left" or "right" or "middle" or "x1" or "x2";
    public static MapperRule Normalize(MapperRule rule) => rule with
    {
        Output = rule.Action == MapperActionKind.MouseMove ? NormalizeMouseAxis(rule.Output) : rule.Output?.Trim() ?? string.Empty
    };
}
