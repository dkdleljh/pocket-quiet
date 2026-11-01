using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using PocketQuiet.Core;
namespace PocketQuiet.Service;

static class Program
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PocketQuiet");
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--watchdog") return Watchdog(int.Parse(args[1]), args[2]);
            if (args.Length > 0 && args[0] == "--request")
            {
                using var pipe = new NamedPipeClientStream(".", Wire.Pipe, PipeDirection.InOut); pipe.Connect(3000);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }; using var reader = new StreamReader(pipe, leaveOpen: true);
                writer.WriteLine(args[1]); Console.WriteLine(reader.ReadLine()); return 0;
            }
            if (args.Contains("--probe") || args.Contains("--restore") || args.Contains("--exercise"))
            {
                using var mutex = new Mutex(false, @"Global\PocketQuiet.Hardware"); bool held = false;
                try
                {
                    try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new InvalidOperationException("제어 서비스가 실행 중입니다. 먼저 중지하세요.");
                    var conflicts = Hardware.Conflicts(); if (conflicts.Length > 0) throw new InvalidOperationException("다른 제어 도구: " + string.Join(", ", conflicts));
                    using var h = new Hardware();
                    if (args.Contains("--restore")) { if (h.Fan == null) throw new InvalidOperationException("지원하지 않는 기기"); h.Fan.Write(null); Console.WriteLine("Firmware automatic verified"); return 0; }
                    if (args.Contains("--exercise"))
                    {
                        if (!h.Supported || h.Bios != "2.11" || h.Fan == null) throw new InvalidOperationException("검증 대상 기기/BIOS가 아닙니다.");
                        try { foreach (double duty in new[] { 60d, 45d, 35d }) { h.Fan.Write(duty); for (int i = 0; i < 6; i++) { Thread.Sleep(1000); var s = h.Read(); Console.WriteLine(JsonSerializer.Serialize(new { duty, s, raw = h.Fan.Raw() }, Wire.Json)); if (s.Temperature is null || s.Temperature >= 80 || s.Rpm is null || s.Rpm == 0) throw new InvalidOperationException("제어 시험 중단: 온도/RPM 기준 미충족"); } } }
                        finally { h.Fan.Write(null); Console.WriteLine("Firmware automatic restored and verified"); }
                        return 0;
                    }
                    for (int i = 0; i < 5; i++) { var s = h.Read(); Console.WriteLine(JsonSerializer.Serialize(new { h.Device, h.Bios, h.Supported, s, raw = h.Fan?.Raw() }, Wire.Json)); Thread.Sleep(1000); }
                    return 0;
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
            if (args.Contains("--console")) { using var runner = new Runner(); runner.Start(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; runner.Stop(); }; while (runner.Running) Thread.Sleep(200); return 0; }
            ServiceBase.Run(new FanService()); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    static int Watchdog(int pid, string eventName)
    {
        using var parent = Process.GetProcessById(pid); using var beat = EventWaitHandle.OpenExisting(eventName); using var done = EventWaitHandle.OpenExisting(eventName + ".stop");
        while (!parent.HasExited)
        {
            int signal = WaitHandle.WaitAny([beat, done], 8000); if (signal == 1) return 0; if (signal == 0) continue;
            if (!parent.HasExited) parent.Kill(); parent.WaitForExit(4000);
            using var mutex = new Mutex(false, @"Global\PocketQuiet.Hardware"); bool held = false;
            try
            {
                try { held = mutex.WaitOne(3000); } catch (AbandonedMutexException) { held = true; }
                if (held) { var bios = new LibreHardwareMonitor.Hardware.SMBios(); if (bios.System?.ManufacturerName == "GPD" && bios.System?.ProductName is "G1628-04" or "G1628-04-L") { using var ec = new Ec(); ec.Write(null); } }
            }
            finally { if (held) mutex.ReleaseMutex(); }
            return 0;
        }
        // Abrupt process death: wait handles may remain valid; restore even without timeout.
        using var m = new Mutex(false, @"Global\PocketQuiet.Hardware"); bool owned = false;
        try { try { owned = m.WaitOne(3000); } catch (AbandonedMutexException) { owned = true; } if (owned) { var b = new LibreHardwareMonitor.Hardware.SMBios(); if (b.System?.ManufacturerName == "GPD" && b.System?.ProductName is "G1628-04" or "G1628-04-L") { using var ec = new Ec(); ec.Write(null); } } } finally { if (owned) m.ReleaseMutex(); }
        return 0;
    }
}
sealed class FanService : ServiceBase
{
    readonly Runner runner = new();
    public FanService() { ServiceName = "PocketQuiet"; CanStop = true; CanShutdown = true; CanHandlePowerEvent = true; AutoLog = true; }
    protected override void OnStart(string[] args) => runner.Start();
    protected override void OnStop() => runner.Stop();
    protected override void OnShutdown() => runner.Stop();
    protected override bool OnPowerEvent(PowerBroadcastStatus state)
    {
        if (state == PowerBroadcastStatus.Suspend) runner.Suspend();
        if (state is PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend) runner.Resume(); return true;
    }
}
sealed class Runner : IDisposable
{
    readonly object gate = new(); readonly ControlEngine engine = new(); CancellationTokenSource stop = new();
    Hardware? hardware; Task? loop, server; Settings settings = new(); Status status = new(); bool suspended, owned;
    readonly string settingsFile = Path.Combine(Program.Data, "settings.json");
    readonly string eventName = @"Global\PocketQuiet.Heartbeat." + Guid.NewGuid().ToString("N");
    EventWaitHandle? heartbeat, shutdown; Process? watchdog; bool manual;
    public bool Running => !stop.IsCancellationRequested;
    public void Start()
    {
        Directory.CreateDirectory(Program.Data);
        try { if (File.Exists(settingsFile)) { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsFile), Wire.Json) ?? new(); settings.Validate(); settings = settings with { Mode = Preset.Firmware }; } }
        catch (Exception ex) { settings = new(); LogError("Settings reset: " + ex.Message); }
        loop = Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning); server = Task.Run(Serve);
    }
    void Run()
    {
        using var mutex = new Mutex(false, @"Global\PocketQuiet.Hardware");
        try
        {
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new InvalidOperationException("다른 Pocket Quiet 제어 프로세스가 실행 중입니다.");
            heartbeat = new EventWaitHandle(false, EventResetMode.AutoReset, eventName); shutdown = new EventWaitHandle(false, EventResetMode.ManualReset, eventName + ".stop");
            watchdog = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--watchdog", Environment.ProcessId.ToString(), eventName } });
            lock (gate) { hardware = new Hardware(); if (hardware.Supported && Hardware.Conflicts().Length == 0) hardware.Fan?.Write(null); }
            while (!stop.IsCancellationRequested)
            {
                var started = Stopwatch.StartNew();
                lock (gate)
                {
                    if (!suspended)
                    {
                        try
                        {
                            var conflicts = Hardware.Conflicts();
                            if (conflicts.Length > 0) { if (manual) Restore(); settings = settings with { Mode = Preset.Firmware }; throw new InvalidOperationException("다른 팬 제어 프로그램을 종료하세요: " + string.Join(", ", conflicts)); }
                            var sample = hardware.Read(); bool ready = hardware.Supported && hardware.Bios == "2.11" && sample.Temperature is >= 5 and < 115 && sample.Rpm is >= 0 and < 12000;
                            if (!ready && settings.Mode != Preset.Firmware) { Restore(); settings = settings with { Mode = Preset.Firmware }; }
                            var decision = engine.Step(sample, settings, DateTimeOffset.UtcNow);
                            if (decision.Automatic) { if (manual) Restore(); } else { manual = true; hardware.Fan!.Write(decision.Output); }
                            if (decision.Fault) { settings = settings with { Mode = Preset.Firmware }; engine.Reset(); }
                            status = new() { Device = hardware.Device, Bios = hardware.Bios, Supported = hardware.Supported, Ready = ready, Time = DateTimeOffset.UtcNow, Temperature = sample.Temperature, Rpm = sample.Rpm, CpuLoad = sample.CpuLoad, Power = sample.Power, Output = decision.Output, ActiveMode = settings.Mode, Settings = settings, Reason = decision.Reason, Error = decision.Fault ? decision.Reason : !ready ? "모델·BIOS 또는 센서 조건이 충족되지 않아 모니터링만 가능합니다." : null };
                            LogSample();
                        }
                        catch (Exception ex) { Fault(ex); }
                    }
                }
                heartbeat.Set(); stop.Token.WaitHandle.WaitOne(Math.Max(10, 1000 - (int)started.ElapsedMilliseconds));
            }
        }
        catch (Exception ex) { lock (gate) Fault(ex); while (!stop.IsCancellationRequested) { heartbeat?.Set(); stop.Token.WaitHandle.WaitOne(1000); } }
        finally { lock (gate) { try { if (manual) Restore(); } catch (Exception ex) { LogError(ex.ToString()); } hardware?.Dispose(); } if (owned) mutex.ReleaseMutex(); if (!manual) shutdown?.Set(); heartbeat?.Set(); }
    }
    void Restore() { hardware?.Fan?.Write(null); manual = false; engine.Reset(); }
    void Fault(Exception ex)
    {
        string message = ex.Message; try { if (manual) Restore(); } catch (Exception restore) { message += " / 자동 복귀 실패: " + restore.Message; }
        settings = settings with { Mode = Preset.Firmware }; engine.Reset(); status = status with { Time = DateTimeOffset.UtcNow, Ready = false, ActiveMode = Preset.Firmware, Output = null, Settings = settings, Error = message, Reason = manual ? "자동 복귀 실패 · 즉시 기기 상태 확인" : "오류 · 수동 제어 중단" }; LogError(message);
    }
    public void Suspend() { lock (gate) { try { if (manual) Restore(); settings = settings with { Mode = Preset.Firmware }; suspended = true; status = status with { Ready = false, ActiveMode = Preset.Firmware, Output = null, Reason = "절전 준비 · 기본 자동" }; } catch (Exception ex) { Fault(ex); } } }
    public void Resume() { lock (gate) { try { hardware?.Dispose(); hardware = new Hardware(); settings = settings with { Mode = Preset.Firmware }; engine.Reset(); suspended = false; } catch (Exception ex) { Fault(ex); suspended = false; } } }
    async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var acl = new PipeSecurity(); acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow)); acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow)); acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                using var pipe = NamedPipeServerStreamAcl.Create(Wire.Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 8192, 8192, acl);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(2500);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true); using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var buffer = new StringBuilder(); while (buffer.Length <= 8192) { var one = new char[1]; int read = await reader.ReadAsync(one.AsMemory(), timeout.Token); if (read == 0 || one[0] == '\n') break; buffer.Append(one[0]); }
                Response response;
                try
                {
                    if (buffer.Length > 8192) throw new ArgumentException("요청 크기 초과"); var request = JsonSerializer.Deserialize<Request>(buffer.ToString(), Wire.Json) ?? throw new ArgumentException("잘못된 요청"); lock (gate)
                    {
                        if (request.Command == "apply")
                        {
                            var next = request.Settings ?? throw new ArgumentException("설정 누락"); next.Validate();
                            if (next.Mode != Preset.Firmware && (!status.Ready || suspended || (DateTimeOffset.UtcNow - status.Time).TotalSeconds > 3)) throw new InvalidOperationException("센서/기기 준비 상태를 확인하세요.");
                            if (Hardware.Conflicts().Length > 0 && next.Mode != Preset.Firmware) throw new InvalidOperationException("다른 팬 제어 프로그램이 실행 중입니다.");
                            if (next.Mode == Preset.Firmware) Restore();
                            File.WriteAllText(settingsFile + ".tmp", JsonSerializer.Serialize(next, Wire.Json)); File.Move(settingsFile + ".tmp", settingsFile, true);
                            settings = next; engine.Reset(); status = status with { Settings = next };
                        }
                        else if (request.Command == "reset") { Restore(); settings = new(); File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, Wire.Json)); status = status with { Settings = settings, ActiveMode = Preset.Firmware, Output = null }; }
                        else if (request.Command != "status") throw new ArgumentException("알 수 없는 명령");
                        response = new(true, status);
                    }
                }
                catch (Exception ex) { response = new(false, status, ex.Message); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Wire.Json).AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogError("IPC: " + ex.Message); await Task.Delay(500); }
        }
    }
    void LogSample() { try { string path = Path.Combine(Program.Data, $"telemetry-{DateTime.UtcNow:yyyy-MM-dd}.csv"); if (!File.Exists(path)) File.WriteAllText(path, "utc,temperature_c,rpm,output_percent,cpu_load,power_w,mode\n"); if (new FileInfo(path).Length < 20_000_000) { var s = status; File.AppendAllText(path, FormattableString.Invariant($"{s.Time:O},{s.Temperature},{s.Rpm},{s.Output},{s.CpuLoad},{s.Power},{s.ActiveMode}\n")); } foreach (var old in Directory.GetFiles(Program.Data, "telemetry-*.csv")) if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-14)) File.Delete(old); } catch { } }
    static void LogError(string message) { try { var p = Path.Combine(Program.Data, "errors.log"); if (File.Exists(p) && new FileInfo(p).Length > 2_000_000) File.Move(p, p + ".old", true); File.AppendAllText(p, $"{DateTimeOffset.UtcNow:O} {message}\n"); } catch { } }
    public void Stop() { stop.Cancel(); if (loop?.Wait(6000) == false) throw new System.TimeoutException("제어 루프가 응답하지 않습니다. 감시 프로세스가 복귀를 시도합니다."); }
    public void Dispose() { Stop(); heartbeat?.Dispose(); shutdown?.Dispose(); watchdog?.Dispose(); stop.Dispose(); }
}
