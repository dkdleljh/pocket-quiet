using PocketQuiet.Core;

namespace PocketQuiet.Control;

public interface IFanHardware : IDisposable
{
    string Device { get; }
    string Bios { get; }
    bool Supported { get; }
    Sample Read();
    // Must throw on unsupported access or a failed read-back verification.
    void Write(double? output);
}
public interface ISettingsStore
{
    Settings Load();
    void Save(Settings settings);
}
public interface IEventSink
{
    void Error(string message);
    void Sample(Status status);
}
public static class DevicePolicy
{
    public static bool CanAccessEc(string? manufacturer, string? model, string? bios) =>
        manufacturer == "GPD" && model is "G1628-04" or "G1628-04-L" && bios == "2.11";
}
