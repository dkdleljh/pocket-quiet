using System.Text.Json;
using PocketQuiet.Core;

namespace PocketQuiet.Control;

public sealed class SettingsStore(string path) : ISettingsStore
{
    public Settings Load()
    {
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Wire.Json)
            ?? throw new InvalidDataException("설정 파일이 비어 있습니다.");
        value.Validate();
        return value with { Mode = Preset.Firmware };
    }

    public void Save(Settings settings)
    {
        settings.Validate();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, settings, Wire.Json);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
