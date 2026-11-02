using PocketQuiet.Core;

namespace PocketQuiet.Control;

// Platform-independent state machine. All hardware transitions are serialized here.
public sealed class ControlSession : IDisposable
{
    readonly object gate = new();
    readonly Func<IFanHardware> createHardware;
    readonly Func<string[]> conflicts;
    readonly Func<DateTimeOffset> clock;
    readonly ISettingsStore store;
    readonly IEventSink log;
    readonly ControlEngine engine = new();
    IFanHardware? hardware;
    Settings settings = new();
    volatile Status status = new();
    bool manual, suspended;
    DateTimeOffset? previousSample;
    string? lastLoggedError;

    public ControlSession(Func<IFanHardware> createHardware, ISettingsStore store, IEventSink log,
        Func<string[]> conflicts, Func<DateTimeOffset>? clock = null)
    {
        this.createHardware = createHardware;
        this.store = store;
        this.log = log;
        this.conflicts = conflicts;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // Reads do not wait on a stalled driver. Status and its settings are copied at the boundary.
    public Status Snapshot
    {
        get { var value = status; return value with { Settings = value.Settings.Copy() }; }
    }
    public bool NeedsRestore { get { lock (gate) return manual; } }

    public void Start()
    {
        lock (gate)
        {
            try { settings = store.Load(); settings.Validate(); settings = settings.Copy() with { Mode = Preset.Firmware }; }
            catch (Exception ex)
            {
                settings = new();
                RecordProtection("설정 읽기 실패 · 기본값 사용: " + ex.Message, false);
                status = status with { PersistenceWarning = "설정을 읽지 못해 기본값을 사용했습니다." };
            }
            InitializeHardware();
        }
    }

    void InitializeHardware()
    {
        try
        {
            hardware = createHardware();
            status = status with { Device = hardware.Device, Bios = hardware.Bios, Supported = hardware.Supported, Settings = settings.Copy() };
            previousSample = null;
            if (hardware.Supported && conflicts().Length == 0) RestoreVerified();
            else status = status with { ControlState = ControlState.Unknown, Ready = false, Reason = "모니터링 전용 · EC 제어 안 함" };
        }
        catch (Exception ex) { Fault("초기화 실패: " + ex.Message); }
    }

    public void Tick(bool watchdogHealthy)
    {
        lock (gate)
        {
            status = status with { Time = clock(), WatchdogHealthy = watchdogHealthy, Suspended = suspended };
            if (suspended) return;
            try
            {
                if (hardware is null) { ClearMeasurements(); return; }
                if (!watchdogHealthy) throw new InvalidOperationException("복구 감시 프로세스가 응답하지 않습니다. 수동 제어를 중단했습니다.");
                string[] tools = conflicts();
                if (tools.Length > 0) throw new InvalidOperationException("다른 팬 제어 프로그램을 종료하세요: " + string.Join(", ", tools));
                Sample sample = hardware.Read();
                string? error = ControlEngine.SampleError(sample, clock(), hardware.Supported);
                if (error is not null) throw new InvalidDataException(error);
                if (previousSample is not null && sample.Time <= previousSample)
                    throw new InvalidDataException("센서 측정 시각이 갱신되지 않았습니다.");
                previousSample = sample.Time;
                if (manual && settings.Mode == Preset.Firmware) RestoreVerified();
                Decision decision = engine.Step(sample, settings, clock());
                if (decision.Fault) throw new InvalidDataException(decision.Reason);
                if (!decision.Automatic)
                {
                    if (!hardware.Supported) throw new InvalidOperationException("검증되지 않은 기기/BIOS에는 출력을 쓸 수 없습니다.");
                    manual = true; // A failed write can still have reached the EC.
                    hardware.Write(decision.Output);
                    status = status with { ControlState = ControlState.Manual };
                }
                status = status with
                {
                    Time = clock(), SampleTime = sample.Time,
                    Temperature = sample.Temperature, Rpm = sample.Rpm,
                    CpuLoad = Finite(sample.CpuLoad), Power = Finite(sample.Power),
                    Ready = hardware.Supported && watchdogHealthy && status.ControlState != ControlState.Unknown,
                    Output = decision.Output, ActiveMode = settings.Mode, Pending = false,
                    Reason = hardware.Supported ? decision.Reason : "미지원 모델/BIOS · 모니터링 전용",
                    Error = hardware.Supported ? null : "이 모델/BIOS는 EC 접근과 수동 제어를 허용하지 않습니다.",
                    Settings = settings.Copy()
                };
                if (decision.Reason.StartsWith("고온 보호") || decision.Reason.StartsWith("팬 회전 재시도"))
                {
                    if (status.LastProtection?.Message != decision.Reason)
                        RecordProtection(decision.Reason, false);
                }
                lastLoggedError = null;
                log.Sample(Snapshot);
            }
            catch (Exception ex) { Fault(ex.Message); }
        }
    }

    static double? Finite(double? value) => value is double number && double.IsFinite(number) ? number : null;

    public Response Handle(Request request)
    {
        if (request.Command == "status") return new(true, Snapshot);
        lock (gate)
        {
            try
            {
                switch (request.Command)
                {
                    case "apply": Apply(request.Settings ?? throw new ArgumentException("설정 누락")); break;
                    case "reset": Apply(new Settings()); break;
                    case "acknowledge": status = status with { LastProtection = null }; break;
                    case "retry": Retry(); break;
                    default: throw new ArgumentException("알 수 없는 명령");
                }
                return new(true, Snapshot);
            }
            catch (Exception ex) { return new(false, Snapshot, ex.Message); }
        }
    }

    void Apply(Settings requested)
    {
        requested.Validate();
        var next = requested.Copy();
        if (next.Mode == Preset.Firmware)
        {
            // Cancel future manual writes BEFORE touching the hardware or disk.
            settings = next;
            status = status with { Settings = settings.Copy(), Pending = false, Ready = false };
            try { RestoreVerified(); }
            catch (Exception ex) { Fault("자동 복귀 확인 실패: " + ex.Message); throw; }
            SaveFirmwareSettings();
            return;
        }
        if (suspended || !status.Ready || !status.WatchdogHealthy || hardware?.Supported != true
            || status.SampleTime is not DateTimeOffset measured || (clock() - measured).TotalSeconds > 3
            || (clock() - status.Time).TotalSeconds > 3)
            throw new InvalidOperationException("기기·센서·복구 감시 상태를 확인하세요.");
        if (conflicts().Length > 0) throw new InvalidOperationException("다른 팬 제어 프로그램이 실행 중입니다.");
        // A failed save must leave the old manual request unchanged.
        store.Save(next);
        if (!manual && settings.Mode == Preset.Firmware)
            engine.BeginManual(new(status.SampleTime!.Value, status.Temperature, status.Rpm, status.CpuLoad, status.Power), next, clock());
        settings = next;
        // Deliberately preserve output, smoothing, timestamps and stall counters on reapply.
        status = status with { Settings = next.Copy(), Pending = true, PersistenceWarning = null };
    }

    void SaveFirmwareSettings()
    {
        try { store.Save(settings); status = status with { PersistenceWarning = null }; }
        catch (Exception ex)
        {
            string warning = "자동 복귀는 확인했지만 설정 저장에 실패했습니다: " + ex.Message;
            status = status with { PersistenceWarning = warning };
            log.Error(warning);
        }
    }

    void RestoreVerified()
    {
        if (hardware?.Supported != true)
            throw new InvalidOperationException("기기/BIOS 또는 하드웨어 초기화 상태 때문에 자동 복귀를 확인할 수 없습니다.");
        hardware.Write(null);
        manual = false;
        engine.Reset();
        status = status with
        {
            Time = clock(), ControlState = ControlState.Firmware, ActiveMode = Preset.Firmware,
            Output = null, Pending = false, Error = null, Reason = "기기 기본 자동 · 복귀 확인"
        };
    }

    void ClearMeasurements() => status = status with
    {
        Ready = false, Temperature = null, Rpm = null, CpuLoad = null, Power = null, Output = null
        // Keep SampleTime as the last valid sample time; never pretend an error is a measurement.
    };

    void RecordProtection(string message, bool verified)
    {
        status = status with { LastProtection = new(clock(), message, verified) };
        if (lastLoggedError != message) log.Error(message);
        lastLoggedError = message;
    }

    public void ReportFault(string message) { lock (gate) Fault(message); }

    void Fault(string message)
    {
        settings = settings with { Mode = Preset.Firmware };
        if (manual)
        {
            try { RestoreVerified(); }
            catch (Exception ex)
            {
                message += " / 자동 복귀 실패: " + ex.Message;
                status = status with { ControlState = ControlState.Unknown };
            }
        }
        engine.Reset();
        ClearMeasurements();
        status = status with
        {
            Time = clock(), Settings = settings.Copy(), ActiveMode = Preset.Firmware,
            Pending = false, Error = message,
            Reason = manual ? "자동 복귀 미확인 · 기기 상태 확인 필요" : "오류 · 수동 제어 중단"
        };
        RecordProtection(message, !manual && status.ControlState == ControlState.Firmware);
    }

    public void Suspend()
    {
        lock (gate)
        {
            suspended = true;
            settings = settings with { Mode = Preset.Firmware };
            try { if (hardware?.Supported == true) RestoreVerified(); }
            catch (Exception ex) { Fault("절전 준비: " + ex.Message); }
            ClearMeasurements();
            status = status with { Time = clock(), Suspended = true, Settings = settings.Copy(), Pending = false, Reason = "절전 준비 · 수동 제어 중단" };
        }
    }

    public void Resume() { lock (gate) Retry(); }

    void Retry()
    {
        settings = settings with { Mode = Preset.Firmware };
        if (manual) RestoreVerified(); // Never discard an outstanding recovery obligation.
        var old = hardware;
        hardware = null;
        status = status with { ControlState = ControlState.Unknown, Ready = false, ActiveMode = Preset.Firmware, Settings = settings.Copy() };
        try { old?.Dispose(); }
        catch (Exception ex) { log.Error("하드웨어 닫기: " + ex.Message); }
        engine.Reset();
        suspended = false;
        ClearMeasurements();
        status = status with { Suspended = false };
        InitializeHardware();
        if (hardware is null || (hardware.Supported && status.ControlState == ControlState.Unknown))
            throw new InvalidOperationException(status.Error ?? "하드웨어 초기화에 실패했습니다.");
    }

    public void Dispose()
    {
        lock (gate)
        {
            settings = settings with { Mode = Preset.Firmware };
            try { if (manual) RestoreVerified(); }
            finally
            {
                var old = hardware;
                hardware = null;
                ClearMeasurements();
                old?.Dispose();
            }
        }
    }
}
