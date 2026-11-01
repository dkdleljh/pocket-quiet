using System.Text.Json;
using System.Text.Json.Serialization;
namespace PocketQuiet.Core;

public enum Preset { Firmware, Adaptive, Quiet, Balanced, Cooling, Custom }
public record CurvePoint(double Temperature, double Output);
public sealed record Settings
{
    public Preset Mode { get; init; } = Preset.Firmware;
    public double Preference { get; init; } = 0;
    public CurvePoint[] Curve { get; init; } = [new(40, 35), new(55, 45), new(65, 55), new(75, 75), new(85, 100)];
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !double.IsFinite(Preference) || Preference < -1 || Preference > 1) throw new ArgumentException("잘못된 프리셋 또는 선호도입니다.");
        if (Curve is null || Curve.Length < 2 || Curve.Length > 12) throw new ArgumentException("곡선은 2~12개 점이 필요합니다.");
        for (int i = 0; i < Curve.Length; i++)
        {
            var p = Curve[i]; if (p is null || !double.IsFinite(p.Temperature) || !double.IsFinite(p.Output) || p.Temperature < 20 || p.Temperature > 85 || p.Output < 30 || p.Output > 100) throw new ArgumentException("온도 20~85°C, 출력 30~100% 범위를 사용하세요.");
            if (i > 0 && (p.Temperature <= Curve[i - 1].Temperature || p.Output < Curve[i - 1].Output)) throw new ArgumentException("온도는 증가해야 하고 출력은 감소할 수 없습니다.");
        }
        if (Curve[^1].Temperature != 85 || Curve[^1].Output != 100) throw new ArgumentException("마지막 점은 85°C / 100%여야 합니다.");
    }
}
public record Sample(DateTimeOffset Time, double? Temperature, int? Rpm, double? CpuLoad, double? Power);
public record Decision(bool Automatic, double? Output, string Reason, bool Fault = false);
public sealed record Status
{
    public string Version { get; init; } = "0.1.0";
    public DateTimeOffset Time { get; init; } = DateTimeOffset.UtcNow;
    public string Device { get; init; } = "확인 중";
    public string Bios { get; init; } = "";
    public bool Supported { get; init; }
    public bool Ready { get; init; }
    public double? Temperature { get; init; }
    public int? Rpm { get; init; }
    public double? CpuLoad { get; init; }
    public double? Power { get; init; }
    public double? Output { get; init; }
    public Preset ActiveMode { get; init; } = Preset.Firmware;
    public string Reason { get; init; } = "기기 기본 자동";
    public string? Error { get; init; }
    public Settings Settings { get; init; } = new();
}
public record Request(string Command, Settings? Settings = null);
public record Response(bool Ok, Status Status, string? Error = null);
public static class Wire
{
    public const string Pipe = "PocketQuiet.v1";
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };
}
