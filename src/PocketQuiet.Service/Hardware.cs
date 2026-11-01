// SPDX-License-Identifier: GPL-2.0-or-later
// EC protocol adapted from Cryolitia/gpd-fan-driver (Cryolitia PukNgae)
// and chenx-dust/FanControl.GPDPlugin (Chenx Dust). See THIRD-PARTY-NOTICES.md.
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using System.Reflection;
using System.Diagnostics;
namespace PocketQuiet.Service;

public sealed class Ec : IDisposable
{
    readonly PawnIo io;
    public Ec()
    {
        if (!PawnIo.IsInstalled) throw new InvalidOperationException("PawnIO 드라이버가 없습니다. 설치 스크립트를 실행하세요.");
        var method = typeof(PawnIo).GetMethod("LoadModuleFromResource", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException("PawnIO loader");
        io = (PawnIo)(method.Invoke(null, [typeof(PawnIo).Assembly, "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin"]) ?? throw new InvalidOperationException("PawnIO 초기화 실패"));
        if (!io.IsLoaded) throw new InvalidOperationException("PawnIO에 접근하지 못했습니다. 드라이버 및 관리자 권한을 확인하세요.");
        Exec("ioctl_select_slot", [1], 0);
    }
    long[] Exec(string name, long[] args, int n)
    {
        var result = new long[n]; int hr = io.ExecuteHr(name, args, (uint)args.Length, result, (uint)n, out uint count);
        if (hr != 0 || count != n) throw new IOException($"PawnIO {name} failed: 0x{hr:X8} ({count}/{n})"); return result;
    }
    void Out(byte register, byte value) => Exec("ioctl_superio_outb", [register, value], 0);
    void Address(ushort address) { Out(0x2E, 0x11); Out(0x2F, (byte)(address >> 8)); Out(0x2E, 0x10); Out(0x2F, (byte)address); Out(0x2E, 0x12); }
    byte ReadByte(ushort address) { Address(address); return (byte)Exec("ioctl_superio_inb", [0x2F], 1)[0]; }
    public int Rpm() { for (int i = 0; i < 3; i++) { byte high = ReadByte(0x478), low = ReadByte(0x479); if (high == ReadByte(0x478)) return (high << 8) | low; } throw new IOException("RPM 값이 읽는 동안 변경되었습니다."); }
    public byte Raw() => ReadByte(0x47A);
    public void Write(double? percent)
    {
        byte value = percent is null ? (byte)0 : (byte)(Math.Round(Math.Clamp(percent.Value, 30, 100) * 243 / 100, MidpointRounding.AwayFromZero) + 1);
        Address(0x47A); Out(0x2F, value);
        if (Raw() != value) throw new IOException("팬 출력 쓰기 검증 실패 / 다른 제어 프로그램과 충돌 가능");
    }
    public void Dispose() => io.Close();
}
public sealed class Hardware : IDisposable
{
    readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    public Ec? Fan { get; private set; }
    public string Device { get; private set; } = "";
    public string Bios { get; private set; } = "";
    public bool Supported { get; private set; }
    public Hardware()
    {
        var bios = new SMBios(); Device = $"{bios.System?.ManufacturerName} {bios.System?.ProductName}";
        Bios = bios.Bios?.Version ?? "unknown";
        Supported = bios.System?.ManufacturerName == "GPD" && bios.System?.ProductName is "G1628-04" or "G1628-04-L";
        if (Supported) Fan = new Ec();
        computer.Open();
    }
    public static string[] Conflicts() => Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } finally { p.Dispose(); } }).Where(n => new[] { "FanControl", "MotionAssistant", "HandheldCompanion", "GPDTool" }.Any(x => n.Equals(x, StringComparison.OrdinalIgnoreCase))).Distinct().ToArray();
    static IEnumerable<IHardware> Walk(IHardware h) { yield return h; foreach (var sub in h.SubHardware) foreach (var x in Walk(sub)) yield return x; }
    public PocketQuiet.Core.Sample Read()
    {
        double? temp = null, load = null, power = null; bool cpuTemperature = false;
        foreach (var h in computer.Hardware.SelectMany(Walk))
        {
            h.Update(); foreach (var s in h.Sensors)
            {
                if (s.Value is not float value || !float.IsFinite(value)) continue;
                if (s.SensorType == SensorType.Temperature && h.HardwareType == HardwareType.Cpu) cpuTemperature = true;
                if (s.SensorType == SensorType.Temperature && (h.HardwareType == HardwareType.Cpu || h.HardwareType == HardwareType.GpuAmd)) temp = Math.Max(temp ?? value, value);
                if (h.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Load && s.Name.Contains("Total")) load = value;
                if (h.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Power && s.Name.Contains("Package")) power = value;
            }
        }
        return new(DateTimeOffset.UtcNow, cpuTemperature ? temp : null, Fan?.Rpm(), load, power);
    }
    public void Dispose() { try { Fan?.Dispose(); } finally { computer.Close(); } }
}
