using System.Diagnostics;
using PocketQuiet.Control;

namespace PocketQuiet.Service;

sealed class Runner : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly TelemetryLog log = new(Program.Data);
    readonly ControlSession session;
    Task? loop, server;
    Watchdog? watchdog;
    public bool Running => !stop.IsCancellationRequested;

    public Runner() => session = new(() => new Hardware(), new SettingsStore(Path.Combine(Program.Data, "settings.json")), log, Hardware.Conflicts);
    public void Start()
    {
        Directory.CreateDirectory(Program.Data);
        loop = Task.Factory.StartNew(Run, TaskCreationOptions.LongRunning);
        server = new PipeServer(session, log).Run(stop.Token);
    }
    void Run()
    {
        using var mutex = new Mutex(false, Program.HardwareMutex);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new InvalidOperationException("다른 Pocket Quiet 제어 프로세스가 실행 중입니다.");
            watchdog = new Watchdog();
            watchdog.TryStart(log);
            session.Start();
            while (!stop.IsCancellationRequested)
            {
                var started = Stopwatch.StartNew();
                session.Tick(watchdog.Healthy);
                watchdog.Pulse();
                watchdog.TryStart(log);
                stop.Token.WaitHandle.WaitOne(Math.Max(10, 1000 - (int)started.ElapsedMilliseconds));
            }
        }
        catch (Exception ex)
        {
            session.ReportFault(ex.Message);
            while (!stop.IsCancellationRequested)
            {
                session.Tick(watchdog?.Healthy == true);
                watchdog?.Pulse();
                stop.Token.WaitHandle.WaitOne(1000);
            }
        }
        finally
        {
            try { session.Dispose(); }
            catch (Exception ex) { log.Error("종료 복구: " + ex.Message); }
            finally
            {
                if (owned) mutex.ReleaseMutex();
                if (!session.NeedsRestore) watchdog?.Complete();
                watchdog?.Pulse();
            }
        }
    }
    public void Suspend() { session.Suspend(); watchdog?.Pause(); }
    public void Resume()
    {
        watchdog?.Resume();
        try { session.Resume(); }
        catch (Exception ex) { session.ReportFault("절전 복귀: " + ex.Message); }
    }
    public void Stop()
    {
        stop.Cancel();
        if (loop?.Wait(6000) == false) throw new TimeoutException("제어 루프가 응답하지 않습니다. 감시 프로세스가 복귀를 시도합니다.");
        server?.Wait(3000);
    }
    public void Dispose() { Stop(); watchdog?.Dispose(); log.Dispose(); stop.Dispose(); }
}
