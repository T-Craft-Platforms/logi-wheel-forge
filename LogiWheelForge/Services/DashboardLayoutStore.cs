using System.IO;
using System.Text.Json;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed class DashboardLayoutStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogiWheelForge", "dashboard-layout.json");
    public async Task<DashboardLayout> LoadAsync()
    {
        if (!File.Exists(_path)) return new();
        await using var stream = File.OpenRead(_path);
        var layout = await JsonSerializer.DeserializeAsync<DashboardLayout>(stream) ?? new();
        DashboardLayoutEngine.Validate(layout);
        return layout;
    }
    public async Task SaveAsync(DashboardLayout layout)
    {
        DashboardLayoutEngine.Validate(layout);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, layout, new JsonSerializerOptions { WriteIndented = true });
                await stream.FlushAsync();
            }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
