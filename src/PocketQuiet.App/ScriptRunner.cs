using System.Diagnostics;
using System.IO;
using System.Text;

namespace PocketQuiet.App;

static class ScriptRunner
{
    public static async Task<string> Run(string name, params string[] arguments)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "scripts", name + ".ps1");
        if (!File.Exists(path)) throw new IOException("배포 폴더의 스크립트가 없습니다: " + name);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned", "-File", path }.Concat(arguments))
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("작업을 시작하지 못했습니다.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException((await error).Trim());
        return await output;
    }
}
