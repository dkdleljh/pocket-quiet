using System.Text.Json;
using PocketQuiet.App;
using PocketQuiet.Control;
using PocketQuiet.Core;

static class RegressionTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var quiet = new Settings { Mode = Preset.Quiet };
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", new Settings { Mode = Preset.Silent })); f.Tick(50);
            check(f.Session.Snapshot.Output == 30, "Verified cold entry into Silent avoids an artificial 60% startup burst");
        }
        var primeEngine = new ControlEngine();
        var primeTime = DateTimeOffset.UtcNow;
        primeEngine.BeginManual(new(primeTime, 50, 0, 0, 0), new Settings { Mode = Preset.Silent }, primeTime);
        check(primeEngine.Step(new(primeTime.AddSeconds(1), 50, 0, 0, 0), new Settings { Mode = Preset.Silent }, primeTime.AddSeconds(1)).Output == 60,
            "A stopped fan retains the conservative startup request");
        using (var f = new Fixture())
        {
            f.Tick(84); check(f.Session.Handle(new("apply", quiet)).Ok, "Manual request accepted with valid sensors and watchdog");
            for (int i = 0; i < 120; i++) f.Tick(84);
            double before = f.Session.Snapshot.Output!.Value;
            f.Session.Handle(new("apply", quiet)); f.Tick(84);
            check(f.Session.Snapshot.Output == before && before >= 90, "Hot reapply preserves the existing output instead of resetting to 60%");
            f.Session.Handle(new("apply", quiet with { Mode = Preset.Silent })); f.Tick(84);
            check(f.Session.Snapshot.Output >= 90, "Changing to Silent never bypasses high-temperature floor");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet));
            for (int i = 0; i < 120; i++) { f.Tick(40); f.Session.Handle(new("apply", quiet)); }
            check(f.Session.Snapshot.Output == 30, "Repeated applies preserve descent and reach the true 30% floor");
            f.Tick(75); check(f.Session.Snapshot.Output >= 65, "75C floor is immediate");
            f.Tick(80); check(f.Session.Snapshot.Output >= 90, "80C floor is immediate");
            f.Tick(85); check(f.Session.Snapshot.Output == 100, "85C emergency remains immediate after cold operation");
        }
        foreach (double temperature in new[] { 40d, 55d, 60d, 65d })
        {
            double Run(Preset mode)
            {
                var engine = new ControlEngine(); var now = DateTimeOffset.UtcNow; Decision? result = null;
                for (int i = 0; i < 180; i++) result = engine.Step(new(now.AddSeconds(i), temperature, 2500, 0, 0), new Settings { Mode = mode }, now.AddSeconds(i));
                return result!.Output!.Value;
            }
            check(Run(Preset.Silent) <= Run(Preset.Quiet), $"Silent output no greater than Quiet at {temperature}C");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick(60);
            var measured = f.Session.Snapshot.SampleTime;
            f.Hardware.ReadFailure = true; f.Tick();
            var s = f.Session.Snapshot;
            check(s.Temperature is null && s.Rpm is null && s.CpuLoad is null && s.Power is null, "Read failure clears every live reading");
            check(s.SampleTime == measured && s.Time > measured, "Fault time never changes last valid sample time");
            check(s.ControlState == ControlState.Firmware && s.ActiveMode == Preset.Firmware, "Sensor failure restores firmware");
            check(s.LastProtection is not null && f.Log.Errors.Count > 0, "Protection reason is retained and logged");
            f.Hardware.ReadFailure = false; f.Tick();
            check(f.Session.Snapshot.Error is null && f.Session.Snapshot.LastProtection is not null, "Recovery does not erase protection history");
            f.Session.Handle(new("acknowledge")); check(f.Session.Snapshot.LastProtection is null, "Protection history can be acknowledged");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick();
            f.Store.FailSave = true;
            var refused = f.Session.Handle(new("apply", quiet with { Mode = Preset.Cooling }));
            check(!refused.Ok && f.Session.Snapshot.Settings.Mode == Preset.Quiet, "Failed manual save leaves prior request intact");
            var restored = f.Session.Handle(new("apply", new Settings()));
            check(restored.Ok && restored.Status.ControlState == ControlState.Firmware && restored.Status.PersistenceWarning is not null,
                "Firmware restore succeeds independently of failed disk persistence");
            f.Tick();
            check(f.Session.Snapshot.ActiveMode == Preset.Firmware && f.Hardware.Writes[^1] is null, "Failed save cannot reactivate manual control");
            var reset = f.Session.Handle(new("reset"));
            check(reset.Ok && reset.Status.Settings.Mode == Preset.Firmware, "Reset also preserves firmware when save fails");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick();
            f.Hardware.RestoreFailure = true;
            var response = f.Session.Handle(new("apply", new Settings()));
            check(!response.Ok && response.Status.ControlState == ControlState.Unknown, "Failed EC restore is never reported as confirmed firmware");
            check(response.Status.Settings.Mode == Preset.Firmware, "Failed restore cancels all future manual requests");
            int count = f.Hardware.Writes.Count; f.Tick();
            check(f.Hardware.Writes.Skip(count).All(output => output is null), "Only recovery writes occur while restore is failing");
            f.Hardware.RestoreFailure = false; f.Tick();
            check(f.Session.Snapshot.ControlState == ControlState.Firmware, "Outstanding restore is retried after hardware recovers");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick();
            f.Tick(watchdog: false);
            check(!f.Session.Snapshot.Ready && f.Session.Snapshot.ControlState == ControlState.Firmware, "Watchdog loss immediately releases manual control");
            check(!f.Session.Handle(new("apply", quiet)).Ok, "Manual mode requires a healthy watchdog");
            f.Tick(); check(f.Session.Snapshot.Ready && f.Session.Snapshot.ActiveMode == Preset.Firmware, "Watchdog recovery never reapplies old manual mode");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick();
            for (int i = 0; i < 5; i++) { f.Session.Handle(new("apply", quiet)); f.Tick(rpm: 0); }
            check(f.Session.Snapshot.ActiveMode == Preset.Firmware && f.Session.Snapshot.LastProtection?.Message.Contains("팬 정지") == true,
                "Reapplying settings cannot reset persistent-stall detection");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick(); f.Session.Suspend();
            check(f.Session.Snapshot.Suspended && f.Session.Snapshot.Temperature is null, "Suspend clears readings and blocks manual mode");
            check(!f.Session.Handle(new("apply", quiet)).Ok, "Apply is rejected while suspended");
            var old = f.Hardware; f.Session.Resume(); f.Tick();
            check(old.Disposed && !ReferenceEquals(old, f.Hardware), "Resume creates new hardware and disposes old sensors");
            check(f.Session.Snapshot.ActiveMode == Preset.Firmware && f.Session.Snapshot.Ready, "Resume starts ready in firmware without restoring manual preset");
        }
        using (var f = new Fixture(supported: false))
        {
            f.Tick(); check(f.Hardware.Writes.Count == 0, "Unsupported BIOS performs no initialization or control writes");
            check(!f.Session.Handle(new("apply", quiet)).Ok, "Unsupported device rejects manual requests");
            check(!f.Session.Handle(new("reset")).Ok, "Unsupported device does not falsely confirm a firmware write");
            f.Session.Dispose(); check(f.Hardware.Writes.Count == 0, "Unsupported device performs no shutdown writes");
        }
        check(DevicePolicy.CanAccessEc("GPD", "G1628-04", "2.11"), "Allowlisted model and BIOS allowed");
        check(!DevicePolicy.CanAccessEc("GPD", "G1628-04", "2.12") && !DevicePolicy.CanAccessEc("GPD", "G1628-04-L", "2.10"), "All EC paths deny unvalidated BIOS versions");
        check(!DevicePolicy.CanAccessEc("Other", "G1628-04", "2.11") && !DevicePolicy.CanAccessEc("GPD", "Unknown", "2.11"), "All EC paths deny unvalidated manufacturer and model");
        using (var session = new ControlSession(() => throw new IOException("driver unavailable"), new MemoryStore(), new MemoryLog(), () => []))
        {
            session.Start(); var response = session.Handle(new("reset"));
            check(!response.Ok && response.Status.ControlState == ControlState.Unknown, "Missing hardware can never produce a successful restore confirmation");
        }
        using (var f = new Fixture())
        {
            f.Conflicting = true; check(!f.Session.Handle(new("apply", quiet)).Ok, "Conflicting tool blocks apply even after a valid sample");
            f.Conflicting = false; f.Session.Handle(new("apply", quiet)); f.Tick();
            f.Conflicting = true; f.Tick(); check(f.Session.Snapshot.ActiveMode == Preset.Firmware, "New conflict releases active manual mode");
        }
        using (var f = new Fixture())
        {
            var snapshot = f.Session.Snapshot; snapshot.Settings.Curve[0] = new(20, 100);
            check(f.Session.Snapshot.Settings.Curve[0].Output != 100, "Snapshot mutation cannot alter controller settings");
            f.Now = f.Now.AddSeconds(4);
            check(!f.Session.Handle(new("apply", quiet)).Ok, "Stale samples block manual requests even while pipe remains responsive");
        }
        using (var f = new Fixture())
        {
            f.Hardware.OverrideTime = f.Now; f.Tick();
            check(!f.Session.Snapshot.Ready && f.Session.Snapshot.Temperature is null, "Repeated hardware timestamp is rejected even in firmware mode");
        }
        using (var f = new Fixture())
        {
            f.Session.Handle(new("apply", quiet)); f.Tick(); f.Hardware.ManualWriteFailure = true; f.Tick();
            check(f.Session.Snapshot.ControlState == ControlState.Firmware && f.Hardware.Writes[^1] is null, "Failed output verification is followed by firmware restore");
        }
        string dir = Path.Combine(Path.GetTempPath(), "pocketquiet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var curve = new[] { new CurvePoint(30, 30), new CurvePoint(85, 100) };
            store.Save(quiet with { Curve = curve, Preference = 0.4 });
            var loaded = store.Load();
            check(loaded.Mode == Preset.Firmware && loaded.Preference == 0.4 && loaded.Curve.Length == 2, "Settings round trip keeps custom values but always starts in firmware");
            store.Save(new()); check(Directory.GetFiles(dir).Length == 1 && store.Load().Curve.Length == 5, "Atomic reset leaves no temporary files");
            File.WriteAllText(Path.Combine(dir, "settings.json"), "broken");
            bool rejected = false; try { store.Load(); } catch (JsonException) { rejected = true; }
            check(rejected, "Corrupt settings are surfaced to the caller");
        }
        finally { Directory.Delete(dir, true); }
        var demo = new DemoClient();
        var applied = await demo.Send(new("apply", new Settings { Mode = Preset.Silent }));
        var resetDemo = await demo.Send(new("reset"));
        check(applied.Ok && applied.Status.ActiveMode == Preset.Silent && resetDemo.Status.ActiveMode == Preset.Firmware, "Demo apply and reset remain fully local without a service");
        check((await demo.Send(new("retry"))).Ok && (await demo.Send(new("acknowledge"))).Ok, "All demo actions are isolated");
        foreach (string bad in new[] { "{\"Mode\":99}", "{\"Mode\":\"Unknown\"}", "{\"Preference\":NaN}" })
        {
            bool rejected = false; try { JsonSerializer.Deserialize<Settings>(bad, Wire.Json)!.Validate(); } catch { rejected = true; }
            check(rejected, "Invalid JSON settings rejected: " + bad);
        }
        check(await LineProtocol.ReadAsync(new StringReader("{\"Command\":\"status\"}\r\n"), default) == "{\"Command\":\"status\"}", "IPC line reader accepts CRLF");
        foreach (string bad in new[] { new string('x', Wire.MaxLineLength + 1) + "\n", "unterminated" })
        {
            bool rejected = false; try { await LineProtocol.ReadAsync(new StringReader(bad), default); } catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
            check(rejected, "Oversized or truncated IPC line rejected");
        }
        string pipeName = "pocketquiet-test-" + Guid.NewGuid().ToString("N");
        using (var server = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous))
        using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
        using (var deadline = new CancellationTokenSource(2000))
        {
            var accept = server.WaitForConnectionAsync(deadline.Token);
            await pipe.ConnectAsync(deadline.Token); await accept;
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(server, leaveOpen: true);
            var incoming = LineProtocol.ReadAsync(reader, deadline.Token);
            await writer.WriteLineAsync("status".AsMemory(), deadline.Token);
            check(await incoming == "status", "IPC parser completes a short line while duplex pipe remains open");
        }
        using var timeout = new CancellationTokenSource(30);
        bool cancelled = false;
        try { await LineProtocol.ReadAsync(new WaitingReader(), timeout.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "Slow IPC reader respects cancellation");
    }

    sealed class WaitingReader : TextReader
    {
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
    sealed class Fixture : IDisposable
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public FakeHardware Hardware = null!;
        public MemoryStore Store = new();
        public MemoryLog Log = new();
        public ControlSession Session;
        public bool Conflicting;
        public Fixture(bool supported = true)
        {
            Session = new(() => Hardware = new FakeHardware(() => Now, supported), Store, Log, () => Conflicting ? ["FanControl"] : [], () => Now);
            Session.Start(); Tick();
        }
        public void Tick(double temperature = 50, int rpm = 2500, bool watchdog = true)
        { Now = Now.AddSeconds(1); Hardware.Temperature = temperature; Hardware.Rpm = rpm; Session.Tick(watchdog); }
        public void Dispose() => Session.Dispose();
    }
    sealed class FakeHardware(Func<DateTimeOffset> clock, bool supported) : IFanHardware
    {
        public string Device => "Test GPD";
        public string Bios => supported ? "2.11" : "9.99";
        public bool Supported => supported;
        public double Temperature = 50;
        public int Rpm = 2500;
        public bool ReadFailure, RestoreFailure, ManualWriteFailure, Disposed;
        public DateTimeOffset? OverrideTime;
        public List<double?> Writes = [];
        public Sample Read()
        {
            if (Disposed || ReadFailure) throw new IOException("sensor failed");
            return new(OverrideTime ?? clock(), Temperature, supported ? Rpm : null, 20, 10);
        }
        public void Write(double? output)
        {
            Writes.Add(output);
            if (!supported || Disposed || (output is null ? RestoreFailure : ManualWriteFailure)) throw new IOException("write failed");
        }
        public void Dispose() => Disposed = true;
    }
    sealed class MemoryStore : ISettingsStore
    {
        public bool FailSave;
        public Settings Stored = new();
        public Settings Load() => Stored.Copy();
        public void Save(Settings settings) { if (FailSave) throw new IOException("disk full"); Stored = settings.Copy(); }
    }
    sealed class MemoryLog : IEventSink
    {
        public List<string> Errors = [];
        public void Error(string message) => Errors.Add(message);
        public void Sample(Status status) { }
    }
}
