using System.Diagnostics;
using PocketQuiet.Control;

namespace PocketQuiet.Service;

sealed class Watchdog : IDisposable
{
    readonly string name = @"Global\PocketQuiet.Heartbeat." + Guid.NewGuid().ToString("N");
    readonly EventWaitHandle beat, stop, ready, paused;
    Process? child;
    DateTime nextStart;
    public Watchdog()
    {
        beat = new(false, EventResetMode.AutoReset, name);
        stop = new(false, EventResetMode.ManualReset, name + ".stop");
        ready = new(false, EventResetMode.ManualReset, name + ".ready");
        paused = new(false, EventResetMode.ManualReset, name + ".paused");
    }
    public bool Healthy
    {
        get { try { return child is not null && !child.HasExited && ready.WaitOne(0); } catch (InvalidOperationException) { return false; } }
    }
    public void Pulse() => beat.Set();
    public void Pause() { paused.Set(); Pulse(); }
    public void Resume() { Pulse(); paused.Reset(); }
    public void TryStart(IEventSink log)
    {
        if (Healthy || DateTime.UtcNow < nextStart) return;
        nextStart = DateTime.UtcNow.AddSeconds(5);
        try
        {
            child?.Dispose();
            child = null;
            ready.Reset();
            Pulse();
            child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true,
                ArgumentList = { "--watchdog", Environment.ProcessId.ToString(), name }
            }) ?? throw new IOException("감시 프로세스를 실행하지 못했습니다.");
            if (!ready.WaitOne(3000) || child.HasExited)
            {
                if (!child.HasExited) child.Kill();
                throw new IOException("감시 프로세스 시작 확인 실패");
            }
        }
        catch (Exception ex) { log.Error("Watchdog: " + ex.Message); }
    }
    public void Complete() => stop.Set();
    public void Dispose() { child?.Dispose(); beat.Dispose(); stop.Dispose(); ready.Dispose(); paused.Dispose(); }

    public static int Run(int pid, string name)
    {
        using var parent = Process.GetProcessById(pid);
        using var beat = EventWaitHandle.OpenExisting(name);
        using var stop = EventWaitHandle.OpenExisting(name + ".stop");
        using var ready = EventWaitHandle.OpenExisting(name + ".ready");
        using var paused = EventWaitHandle.OpenExisting(name + ".paused");
        ready.Set();
        while (true)
        {
            if (stop.WaitOne(0)) return 0;
            if (parent.HasExited) break;
            int signal = WaitHandle.WaitAny([stop, beat], 8000);
            if (signal == 0) return 0;
            if (signal == 1 || paused.WaitOne(0)) continue;
            if (!parent.HasExited) parent.Kill();
            parent.WaitForExit(4000);
            break;
        }
        using var mutex = new Mutex(false, Program.HardwareMutex);
        bool owned = false;
        try
        {
            try { owned = mutex.WaitOne(3000); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) return 0; // A replacement controller owns recovery now.
            var bios = new LibreHardwareMonitor.Hardware.SMBios();
            if (DevicePolicy.CanAccessEc(bios.System?.ManufacturerName, bios.System?.ProductName, bios.Bios?.Version))
            {
                using var ec = new Ec();
                ec.Write(null);
                Program.LogFailure("Watchdog: firmware restoration verified.");
            }
        }
        finally { if (owned) mutex.ReleaseMutex(); }
        return 0;
    }
}
