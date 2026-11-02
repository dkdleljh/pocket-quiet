// SPDX-License-Identifier: GPL-2.0-or-later
// EC protocol adapted from Cryolitia/gpd-fan-driver (Cryolitia PukNgae)
// and chenx-dust/FanControl.GPDPlugin (Chenx Dust). See THIRD-PARTY-NOTICES.md.
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using PocketQuiet.Control;
using PocketQuiet.Core;
using System.Reflection;
using System.Diagnostics;

namespace PocketQuiet.Service;

public sealed class Ec : IDisposable
{
    readonly PawnIo io;
    public Ec()
    {
        var bios = new SMBios();
        if (!DevicePolicy.CanAccessEc(bios.System?.ManufacturerName, bios.System?.ProductName, bios.Bios?.Version))
            throw new InvalidOperationException("이 모델/BIOS에서는 EC 접근을 허용하지 않습니다.");
        if (!PawnIo.IsInstalled) throw new InvalidOperationException("PawnIO 드라이버가 없습니다. 설치 스크립트를 실행하세요.");
        var method = typeof(PawnIo).GetMethod("LoadModuleFromResource", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("PawnIO loader");
        io = (PawnIo)(method.Invoke(null, [typeof(PawnIo).Assembly, "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin"])
            ?? throw new InvalidOperationException("PawnIO 초기화 실패"));
        try
        {
            if (!io.IsLoaded) throw new InvalidOperationException("PawnIO에 접근하지 못했습니다. 드라이버 및 관리자 권한을 확인하세요.");
            Exec("ioctl_select_slot", [1], 0);
        }
        catch { io.Close(); throw; }
    }
    long[] Exec(string name, long[] args, int length)
    {
        var result = new long[length];
        int hr = io.ExecuteHr(name, args, (uint)args.Length, result, (uint)length, out uint count);
        if (hr != 0 || count != length) throw new IOException($"PawnIO {name} failed: 0x{hr:X8} ({count}/{length})");
        return result;
    }
    void Out(byte register, byte value) => Exec("ioctl_superio_outb", [register, value], 0);
    void Address(ushort address)
    {
        Out(0x2E, 0x11); Out(0x2F, (byte)(address >> 8));
        Out(0x2E, 0x10); Out(0x2F, (byte)address); Out(0x2E, 0x12);
    }
    byte ReadByte(ushort address) { Address(address); return (byte)Exec("ioctl_superio_inb", [0x2F], 1)[0]; }
    public int Rpm()
    {
        for (int i = 0; i < 3; i++)
        {
            byte high = ReadByte(0x478), low = ReadByte(0x479);
            if (high == ReadByte(0x478)) return (high << 8) | low;
        }
        throw new IOException("RPM 값이 읽는 동안 변경되었습니다.");
    }
    public byte Raw() => ReadByte(0x47A);
    public void Write(double? percent)
    {
        if (percent is double p && (!double.IsFinite(p) || p is < 30 or > 100))
            throw new ArgumentOutOfRangeException(nameof(percent));
        byte value = percent is null ? (byte)0 : (byte)(Math.Round(percent.Value * 243 / 100, MidpointRounding.AwayFromZero) + 1);
        Address(0x47A); Out(0x2F, value);
        if (Raw() != value) throw new IOException("팬 출력 쓰기 검증 실패 / 다른 제어 프로그램과 충돌 가능");
    }
    public void Dispose() => io.Close();
}

public sealed class Hardware : IFanHardware
{
    readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    public Ec? Fan { get; private set; }
    public string Device { get; }
    public string Bios { get; }
    public bool Supported { get; }
    public Hardware()
    {
        var bios = new SMBios();
        Device = $"{bios.System?.ManufacturerName} {bios.System?.ProductName}";
        Bios = bios.Bios?.Version ?? "unknown";
        Supported = DevicePolicy.CanAccessEc(bios.System?.ManufacturerName, bios.System?.ProductName, Bios);
        try
        {
            if (Supported) Fan = new Ec();
            computer.Open();
        }
        catch { Dispose(); throw; }
    }
    public void Write(double? output)
    {
        if (!Supported || Fan is null) throw new InvalidOperationException("미지원 기기/BIOS: EC 출력 쓰기를 거부했습니다.");
        Fan.Write(output);
    }
    public static string[] Conflicts() => Process.GetProcesses().Select(process =>
    {
        try { return process.ProcessName; }
        catch { return ""; }
        finally { process.Dispose(); }
    }).Where(name => new[] { "FanControl", "MotionAssistant", "HandheldCompanion", "GPDTool" }
        .Contains(name, StringComparer.OrdinalIgnoreCase)).Distinct().ToArray();

    static IEnumerable<IHardware> Walk(IHardware hardware)
    {
        yield return hardware;
        foreach (var child in hardware.SubHardware)
            foreach (var item in Walk(child)) yield return item;
    }
    public Sample Read()
    {
        double? temp = null, load = null, power = null;
        bool cpuTemperature = false;
        foreach (var hardware in computer.Hardware.SelectMany(Walk))
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is not float value || !float.IsFinite(value)) continue;
                if (sensor.SensorType == SensorType.Temperature && hardware.HardwareType == HardwareType.Cpu) cpuTemperature = true;
                if (sensor.SensorType == SensorType.Temperature && hardware.HardwareType is HardwareType.Cpu or HardwareType.GpuAmd)
                    temp = Math.Max(temp ?? value, value);
                if (hardware.HardwareType == HardwareType.Cpu && sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total")) load = value;
                if (hardware.HardwareType == HardwareType.Cpu && sensor.SensorType == SensorType.Power && sensor.Name.Contains("Package")) power = value;
            }
        }
        int? rpm = Fan?.Rpm();
        return new(DateTimeOffset.UtcNow, cpuTemperature ? temp : null, rpm, load, power);
    }
    public void Dispose() { try { Fan?.Dispose(); } finally { computer.Close(); } }
}
