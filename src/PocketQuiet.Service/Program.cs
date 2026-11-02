using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using PocketQuiet.Core;

namespace PocketQuiet.Service;

static class Program
{
    public const string HardwareMutex = @"Global\PocketQuiet.Hardware";
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PocketQuiet");
    public static int Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--watchdog")
            {
                if (args.Length != 3 || !int.TryParse(args[1], out int pid)) throw new ArgumentException("Invalid watchdog arguments");
                return Watchdog.Run(pid, args[2]);
            }
            if (args.FirstOrDefault() == "--request")
            {
                if (args.Length != 2) throw new ArgumentException("--request requires one JSON request");
                return Request(args[1]).GetAwaiter().GetResult();
            }
            if (args.Any(arg => arg is "--probe" or "--restore" or "--restore-if-supported" or "--exercise")) return Diagnose(args);
            if (args.Contains("--console"))
            {
                using var runner = new Runner();
                runner.Start();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; runner.Stop(); };
                while (runner.Running) Thread.Sleep(200);
                return 0;
            }
            ServiceBase.Run(new FanService());
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); LogFailure(ex.Message); return 1; }
    }
    static async Task<int> Request(string json)
    {
        using var timeout = new CancellationTokenSource(5000);
        using var pipe = new NamedPipeClientStream(".", Wire.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(json.AsMemory(), timeout.Token);
        string line = await LineProtocol.ReadAsync(reader, timeout.Token, 32768);
        Console.WriteLine(line);
        return JsonSerializer.Deserialize<Response>(line, Wire.Json)?.Ok == true ? 0 : 1;
    }
    static int Diagnose(string[] args)
    {
        using var mutex = new Mutex(false, HardwareMutex);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new InvalidOperationException("제어 서비스가 실행 중입니다. 먼저 중지하세요.");
            var conflicts = Hardware.Conflicts();
            if (conflicts.Length > 0) throw new InvalidOperationException("다른 제어 도구: " + string.Join(", ", conflicts));
            if (args.Contains("--restore-if-supported"))
            {
                var bios = new LibreHardwareMonitor.Hardware.SMBios();
                if (!PocketQuiet.Control.DevicePolicy.CanAccessEc(bios.System?.ManufacturerName, bios.System?.ProductName, bios.Bios?.Version))
                {
                    Console.WriteLine("Unsupported model/BIOS: no EC access performed.");
                    return 0;
                }
            }
            if (args.Contains("--restore") || args.Contains("--restore-if-supported"))
            {
                // Recovery does not depend on initializing unrelated monitoring sensors.
                using var ec = new Ec();
                ec.Write(null);
                Console.WriteLine("Firmware automatic verified");
                return 0;
            }
            using var hardware = new Hardware();
            if (args.Contains("--exercise"))
            {
                if (!hardware.Supported) throw new InvalidOperationException("검증 대상 기기/BIOS가 아닙니다.");
                try
                {
                    foreach (double duty in new[] { 60d, 45d, 35d })
                    {
                        hardware.Write(duty);
                        for (int i = 0; i < 6; i++)
                        {
                            Thread.Sleep(1000);
                            var sample = hardware.Read();
                            Console.WriteLine(JsonSerializer.Serialize(new { duty, sample, raw = hardware.Fan!.Raw() }, Wire.Json));
                            if (ControlEngine.SampleError(sample, DateTimeOffset.UtcNow) is not null || sample.Temperature >= 80 || sample.Rpm == 0)
                                throw new InvalidOperationException("제어 시험 중단: 온도/RPM 기준 미충족");
                        }
                    }
                }
                finally { hardware.Write(null); Console.WriteLine("Firmware automatic restored and verified"); }
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    var sample = hardware.Read();
                    Console.WriteLine(JsonSerializer.Serialize(new { hardware.Device, hardware.Bios, hardware.Supported, sample, raw = hardware.Fan?.Raw() }, Wire.Json));
                    Thread.Sleep(1000);
                }
            }
            return 0;
        }
        finally { if (owned) mutex.ReleaseMutex(); }
    }
    public static void LogFailure(string message)
    {
        try
        {
            Directory.CreateDirectory(Data);
            string path = Path.Combine(Data, "watchdog.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}\n");
        }
        catch { }
    }
}
sealed class FanService : ServiceBase
{
    readonly Runner runner = new();
    public FanService() { ServiceName = "PocketQuiet"; CanStop = true; CanShutdown = true; CanHandlePowerEvent = true; AutoLog = true; }
    protected override void OnStart(string[] args) => runner.Start();
    protected override void OnStop() => runner.Stop();
    protected override void OnShutdown() => runner.Stop();
    protected override void Dispose(bool disposing) { if (disposing) runner.Dispose(); base.Dispose(disposing); }
    protected override bool OnPowerEvent(PowerBroadcastStatus state)
    {
        if (state == PowerBroadcastStatus.Suspend) runner.Suspend();
        if (state is PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend) runner.Resume();
        return true;
    }
}
