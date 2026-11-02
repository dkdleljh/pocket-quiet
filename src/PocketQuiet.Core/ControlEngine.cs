namespace PocketQuiet.Core;

// All thresholds are application policy, not CPU manufacturer specifications.
public sealed class ControlEngine
{
    double? average, last;
    double output = 60, downSeconds;
    int stopped;
    bool decelerating;
    DateTimeOffset? previous;

    public void Reset()
    {
        average = last = null;
        output = 60;
        downSeconds = 0;
        stopped = 0;
        decelerating = false;
        previous = null;
    }

    // A running fan at a recently verified low temperature needs no artificial 60% burst.
    // Warm, missing or stopped-fan samples retain the conservative startup state.
    public void BeginManual(Sample sample, Settings settings, DateTimeOffset now)
    {
        settings.Validate();
        Reset();
        if (settings.Mode == Preset.Firmware || SampleError(sample, now) is not null
            || sample.Temperature > 55 || sample.Rpm <= 0) return;
        double temperature = sample.Temperature!.Value;
        var curve = settings.Mode == Preset.Custom ? settings.Curve : Presets.Curve(settings.Mode);
        double effective = temperature + (settings.Mode == Preset.Adaptive ? settings.Preference * 5 : 0);
        output = Math.Clamp(Interpolate(curve, effective), 30, 100);
        average = last = temperature;
        previous = sample.Time;
    }

    public static string? SampleError(Sample sample, DateTimeOffset now, bool requireRpm = true)
    {
        if (sample.Temperature is not double t || !double.IsFinite(t) || t is < 5 or > 115
            || (now - sample.Time).TotalSeconds > 3 || sample.Time > now.AddSeconds(1))
            return "온도 데이터 오류 · 자동 제어 복귀";
        if (requireRpm && (sample.Rpm is null || sample.Rpm is < 0 or > 12000))
            return "팬 RPM 데이터 오류 · 자동 제어 복귀";
        return null;
    }

    public Decision Step(Sample sample, Settings settings, DateTimeOffset now)
    {
        settings.Validate();
        if (settings.Mode == Preset.Firmware)
        {
            Reset();
            return new(true, null, "기기 기본 자동");
        }
        if (SampleError(sample, now) is string error) return new(true, null, error, true);
        if (previous is not null && sample.Time <= previous.Value)
            return new(true, null, "갱신되지 않은 데이터 · 자동 제어 복귀", true);
        double dt = previous is null ? 1 : Math.Clamp((sample.Time - previous.Value).TotalSeconds, 0.05, 3);
        previous = sample.Time;
        double t = sample.Temperature!.Value;
        stopped = sample.Rpm == 0 ? stopped + 1 : 0;
        if (stopped >= 5) return new(true, null, "팬 정지 지속 · 자동 제어 복귀", true);
        if (stopped >= 2 || t >= 85)
        {
            output = 100;
            average = last = t;
            downSeconds = 0;
            decelerating = false;
            return new(false, 100, stopped >= 2 ? "팬 회전 재시도 · 최대 냉각" : "고온 보호 · 최대 냉각");
        }
        double slope = last is null ? 0 : (t - last.Value) / dt;
        last = t;
        average = average is null ? t : average + (1 - Math.Exp(-dt / 4)) * (t - average);
        double effective = average.Value;
        string reason = settings.Mode == Preset.Silent ? "최소 소음 · 저온 회전 억제" : "온도에 따른 자동 조절";
        if (settings.Mode == Preset.Adaptive)
        {
            effective += Math.Clamp(slope * 3, 0, 8) + settings.Preference * 5;
            reason = slope > 0.5 ? "온도 상승 감지 · 냉각 강화" : t < 55 ? "가벼운 작업 · 소음 억제" : "지속 발열 · 온도 안정화";
        }
        var curve = settings.Mode == Preset.Custom ? settings.Curve : Presets.Curve(settings.Mode);
        double target = Interpolate(curve, effective);
        // High-temperature floors apply immediately, independent of smoothing or preset changes.
        double floor = t >= 80 ? 90 : t >= 75 ? 65 : 30;
        target = Math.Max(target, floor);
        output = Math.Max(output, floor);
        if (target > output)
        {
            output = Math.Min(target, output + 12 * dt);
            downSeconds = 0;
            decelerating = false;
        }
        else if (target < output && (target < output - 3 || decelerating || target == 30))
        {
            downSeconds += dt;
            if (downSeconds >= 20)
            {
                // Once descent starts, reach the curve instead of stopping 3% above it.
                decelerating = true;
                output = Math.Max(target, output - 1.5 * dt);
            }
            else reason += " · 감속 대기";
        }
        else
        {
            downSeconds = 0;
            decelerating = false;
        }
        output = Math.Clamp(output, 30, 100);
        return new(false, Math.Round(output, 1), reason);
    }

    public static double Interpolate(CurvePoint[] curve, double t)
    {
        if (t <= curve[0].Temperature) return curve[0].Output;
        for (int i = 1; i < curve.Length; i++)
        {
            if (t > curve[i].Temperature) continue;
            var a = curve[i - 1];
            var b = curve[i];
            return a.Output + (b.Output - a.Output) * (t - a.Temperature) / (b.Temperature - a.Temperature);
        }
        return curve[^1].Output;
    }
}
