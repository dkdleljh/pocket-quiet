using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PocketQuiet.Core;

namespace PocketQuiet.App;

public interface IAppClient
{
    Task<Response> Send(Request request, CancellationToken cancellationToken = default);
}
public sealed class ServiceClient : IAppClient
{
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task<Response> Send(Request request, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(3500); // Includes queueing, connection, request and response.
        await gate.WaitAsync(timeout.Token);
        try
        {
            using var pipe = new NamedPipeClientStream(".", Wire.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, Wire.Json).AsMemory(), timeout.Token);
            string line = await LineProtocol.ReadAsync(reader, timeout.Token, 32768);
            return JsonSerializer.Deserialize<Response>(line, Wire.Json) ?? throw new IOException("서비스 응답이 없습니다.");
        }
        finally { gate.Release(); }
    }
}

// Every demo action is isolated here; no pipe or hardware client exists in this class.
public sealed class DemoClient : IAppClient
{
    Settings settings = new();
    readonly ControlEngine engine = new();
    DateTimeOffset? last;
    Status status = new() { Device = "데모 · 실제 하드웨어 제어 없음", Bios = "DEMO", ControlState = ControlState.Firmware };
    public Task<Response> Send(Request request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            switch (request.Command)
            {
                case "apply":
                    var next = request.Settings ?? throw new ArgumentException("설정 누락");
                    next.Validate(); settings = next.Copy(); break;
                case "reset": settings = new(); engine.Reset(); break;
                case "acknowledge": status = status with { LastProtection = null }; break;
                case "retry": settings = settings with { Mode = Preset.Firmware }; engine.Reset(); break;
                case "status": break;
                default: throw new ArgumentException("알 수 없는 명령");
            }
            var now = DateTimeOffset.UtcNow;
            if (last is not null && now <= last) now = last.Value.AddTicks(1);
            last = now;
            double t = 52 + Math.Sin(now.TimeOfDay.TotalSeconds / 12) * 8;
            var decision = engine.Step(new(now, t, 2400, 22, 12), settings, now);
            status = status with
            {
                Time = now, SampleTime = now, Temperature = t, Rpm = 2400, CpuLoad = 22, Power = 12,
                Supported = true, Ready = true, WatchdogHealthy = true, ActiveMode = settings.Mode,
                ControlState = settings.Mode == Preset.Firmware ? ControlState.Firmware : ControlState.Manual,
                Settings = settings.Copy(), Output = decision.Output,
                Reason = "미리보기 · " + decision.Reason
            };
            return Task.FromResult(new Response(true, status));
        }
        catch (Exception ex) { return Task.FromResult(new Response(false, status, ex.Message)); }
    }
}
