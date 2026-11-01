namespace PocketQuiet.Core;
// All thresholds are application policy, not CPU manufacturer specifications.
public sealed class ControlEngine
{
    double? average, last; double output = 60, downSeconds; int stopped; DateTimeOffset? previous;
    public void Reset() { average = last = null; output = 60; downSeconds = 0; stopped = 0; previous = null; }
    public Decision Step(Sample s, Settings settings, DateTimeOffset now)
    {
        settings.Validate();
        if (settings.Mode == Preset.Firmware) { Reset(); return new(true, null, "기기 기본 자동"); }
        if (s.Temperature is not double t || !double.IsFinite(t) || t < 5 || t > 115 || (now - s.Time).TotalSeconds > 3 || s.Time > now.AddSeconds(1)) return new(true, null, "온도 데이터 오류 · 자동 제어 복귀", true);
        if (s.Rpm is null || s.Rpm < 0 || s.Rpm > 12000) return new(true, null, "팬 RPM 데이터 오류 · 자동 제어 복귀", true);
        var dt = previous is null ? 1 : Math.Clamp((s.Time - previous.Value).TotalSeconds, 0.05, 3);
        if (previous is not null && s.Time <= previous.Value) return new(true, null, "갱신되지 않은 데이터 · 자동 제어 복귀", true);
        previous = s.Time;
        stopped = s.Rpm == 0 ? stopped + 1 : 0;
        if (stopped >= 5) return new(true, null, "팬 정지 지속 · 자동 제어 복귀", true);
        if (stopped >= 2) { output = 100; return new(false, 100, "팬 회전 재시도 · 냉각 우선"); }
        if (t >= 85) { output = 100; average = t; last = t; downSeconds = 0; return new(false, 100, "고온 보호 · 최대 냉각"); }
        double slope = last is null ? 0 : (t - last.Value) / dt; last = t;
        average = average is null ? t : average + (1 - Math.Exp(-dt / 4)) * (t - average);
        double effective = average.Value;
        string reason = "온도에 따른 자동 조절";
        CurvePoint[] curve = settings.Mode switch
        {
            Preset.Quiet => [new(40, 30), new(55, 35), new(65, 45), new(75, 65), new(85, 100)],
            Preset.Cooling => [new(35, 45), new(50, 60), new(65, 75), new(75, 90), new(85, 100)],
            Preset.Custom => settings.Curve,
            _ => [new(40, 35), new(55, 45), new(65, 55), new(75, 75), new(85, 100)]
        };
        if (settings.Mode == Preset.Adaptive)
        {
            effective += Math.Clamp(slope * 3, 0, 8) + settings.Preference * 5;
            reason = slope > 0.5 ? "온도 상승 감지 · 냉각 강화" : t < 55 ? "가벼운 작업 · 소음 억제" : "지속 발열 · 온도 안정화";
        }
        double target = Interpolate(curve, effective);
        // Never let averaging conceal sustained high temperature.
        if (t >= 80) target = Math.Max(target, 90);
        else if (t >= 75) target = Math.Max(target, 65);
        if (target > output) { output = Math.Min(target, output + 12 * dt); downSeconds = 0; }
        else if (target < output - 3) { downSeconds += dt; if (downSeconds >= 20) output = Math.Max(target, output - 1.5 * dt); else reason += " · 감속 대기"; }
        else downSeconds = 0;
        output = Math.Clamp(output, 30, 100);
        return new(false, Math.Round(output, 1), reason);
    }
    public static double Interpolate(CurvePoint[] curve, double t)
    {
        if (t <= curve[0].Temperature) return curve[0].Output;
        for (int i = 1; i < curve.Length; i++) if (t <= curve[i].Temperature) { var a = curve[i - 1]; var b = curve[i]; return a.Output + (b.Output - a.Output) * (t - a.Temperature) / (b.Temperature - a.Temperature); }
        return curve[^1].Output;
    }
}
