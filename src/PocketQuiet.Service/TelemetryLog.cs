using System.Threading.Channels;
using PocketQuiet.Control;
using PocketQuiet.Core;

namespace PocketQuiet.Service;

// Disk operations never run on the controller thread or under its lock.
sealed class TelemetryLog : IEventSink, IDisposable
{
    readonly Channel<Status> samples = Channel.CreateBounded<Status>(new BoundedChannelOptions(120) { FullMode = BoundedChannelFullMode.DropOldest });
    readonly Channel<string> errors = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
    readonly Task writer;
    readonly string directory;
    DateTime nextCleanup;
    public TelemetryLog(string directory) { this.directory = directory; writer = Task.Run(WriteLoop); }
    public void Error(string message) => errors.Writer.TryWrite($"{DateTimeOffset.UtcNow:O} {message.Replace('\r', ' ').Replace('\n', ' ')}");
    public void Sample(Status status) => samples.Writer.TryWrite(status);
    async Task WriteLoop()
    {
        while (true)
        {
            while (errors.Reader.TryRead(out string? error)) WriteError(error);
            while (samples.Reader.TryRead(out Status? sample)) WriteSample(sample);
            if (samples.Reader.Completion.IsCompleted && errors.Reader.Completion.IsCompleted) return;
            await Task.Delay(200);
        }
    }
    void WriteError(string message)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, message + "\n");
        }
        catch { /* Logging must not interrupt cooling. */ }
    }
    void WriteSample(Status sample)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"telemetry-{DateTime.UtcNow:yyyy-MM-dd}.csv");
            if (!File.Exists(path)) File.WriteAllText(path, "utc,temperature_c,rpm,output_percent,cpu_load,power_w,mode\n");
            if (new FileInfo(path).Length < 20_000_000)
                File.AppendAllText(path, FormattableString.Invariant($"{sample.SampleTime:O},{sample.Temperature},{sample.Rpm},{sample.Output},{sample.CpuLoad},{sample.Power},{sample.ActiveMode}\n"));
            if (DateTime.UtcNow < nextCleanup) return;
            nextCleanup = DateTime.UtcNow.AddHours(1);
            foreach (string old in Directory.GetFiles(directory, "telemetry-*.csv"))
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-14)) File.Delete(old);
        }
        catch { }
    }
    public void Dispose()
    {
        samples.Writer.TryComplete(); errors.Writer.TryComplete();
        writer.Wait(TimeSpan.FromSeconds(2));
    }
}
