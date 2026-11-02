using System.Text.Json;
using System.Text.Json.Serialization;

namespace PocketQuiet.Core;

// Append modes to preserve the numeric values used by older settings.
public enum Preset { Firmware, Adaptive, Quiet, Balanced, Cooling, Custom, Silent }
public enum ControlState { Unknown, Firmware, Manual }
public record CurvePoint(double Temperature, double Output);
public record ProtectionEvent(DateTimeOffset Time, string Message, bool FirmwareVerified);

public sealed record Settings
{
    public Preset Mode { get; init; } = Preset.Firmware;
    public double Preference { get; init; }
    public CurvePoint[] Curve { get; init; } = Presets.Curve(Preset.Balanced);

    public Settings Copy() => this with { Curve = (CurvePoint[])Curve.Clone() };

    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !double.IsFinite(Preference) || Preference is < -1 or > 1)
            throw new ArgumentException("잘못된 프리셋 또는 선호도입니다.");
        if (Curve is null || Curve.Length is < 2 or > 12)
            throw new ArgumentException("곡선은 2~12개 점이 필요합니다.");
        for (int i = 0; i < Curve.Length; i++)
        {
            var point = Curve[i];
            if (point is null || !double.IsFinite(point.Temperature) || !double.IsFinite(point.Output)
                || point.Temperature is < 20 or > 85 || point.Output is < 30 or > 100)
                throw new ArgumentException($"{i + 1}번째 점: 온도 20~85°C, 출력 30~100%를 입력하세요.");
            if (i > 0 && (point.Temperature <= Curve[i - 1].Temperature || point.Output < Curve[i - 1].Output))
                throw new ArgumentException($"{i + 1}번째 점: 온도는 증가하고 출력은 감소하지 않아야 합니다.");
        }
        if (Curve[^1].Temperature != 85 || Curve[^1].Output != 100)
            throw new ArgumentException("마지막 점은 85°C / 100%여야 합니다.");
    }
}

public static class Presets
{
    public static readonly Preset[] DisplayOrder = [Preset.Firmware, Preset.Silent, Preset.Quiet, Preset.Adaptive, Preset.Balanced, Preset.Cooling, Preset.Custom];
    public static string Label(Preset mode) => mode switch
    {
        Preset.Firmware => "기기 기본 자동", Preset.Silent => "무소음 · 최소 소음",
        Preset.Adaptive => "AI 자동 · 적응형", Preset.Quiet => "저소음",
        Preset.Balanced => "균형", Preset.Cooling => "냉각 우선", Preset.Custom => "사용자 지정",
        _ => "알 수 없는 모드"
    };
    public static CurvePoint[] Curve(Preset mode) => mode switch
    {
        Preset.Silent => [new(40, 30), new(55, 30), new(65, 38), new(75, 65), new(85, 100)],
        Preset.Quiet => [new(40, 30), new(55, 35), new(65, 45), new(75, 65), new(85, 100)],
        Preset.Cooling => [new(35, 45), new(50, 60), new(65, 75), new(75, 90), new(85, 100)],
        _ => [new(40, 35), new(55, 45), new(65, 55), new(75, 75), new(85, 100)]
    };
}

public record Sample(DateTimeOffset Time, double? Temperature, int? Rpm, double? CpuLoad, double? Power);
public record Decision(bool Automatic, double? Output, string Reason, bool Fault = false);
public sealed record Status
{
    public string Version { get; init; } = "0.2.0";
    public int ProtocolVersion { get; init; } = 2;
    // Time is the controller tick time; SampleTime only advances after a valid read.
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SampleTime { get; init; }
    public string Device { get; init; } = "확인 중";
    public string Bios { get; init; } = "";
    public bool Supported { get; init; }
    public bool Ready { get; init; }
    public bool WatchdogHealthy { get; init; }
    public bool Suspended { get; init; }
    public bool Pending { get; init; }
    public double? Temperature { get; init; }
    public int? Rpm { get; init; }
    public double? CpuLoad { get; init; }
    public double? Power { get; init; }
    public double? Output { get; init; }
    public Preset ActiveMode { get; init; } = Preset.Firmware;
    public ControlState ControlState { get; init; } = ControlState.Unknown;
    public string Reason { get; init; } = "상태 확인 중";
    public string? Error { get; init; }
    public string? PersistenceWarning { get; init; }
    public ProtectionEvent? LastProtection { get; init; }
    public Settings Settings { get; init; } = new();
}
public record Request(string Command, Settings? Settings = null);
public record Response(bool Ok, Status Status, string? Error = null);
public static class Wire
{
    public const string Pipe = "PocketQuiet.v1";
    public const int MaxLineLength = 8192;
    public static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }, MaxDepth = 16
    };
}
