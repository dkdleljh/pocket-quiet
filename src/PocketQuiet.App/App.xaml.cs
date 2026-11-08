using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace PocketQuiet.App;

public partial class App : System.Windows.Application
{
    Mutex? instance;
    CancellationTokenSource? activationStop;
    bool ownsInstance;
    const string Activation = "PocketQuiet.UI.Activate.v2";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A signable executable entry point avoids dependence on downloaded .cmd launchers.
        // Demo flags cannot be combined with maintenance commands.
        string? maintenance = e.Args.Length == 1 ? e.Args[0] switch
        {
            "--install" => "Install", "--recover" => "Recover", "--uninstall" => "Uninstall", _ => null
        } : null;
        if (maintenance is not null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RunMaintenance(maintenance);
            return;
        }
        // Demo previews can run alongside the real UI and never send control requests.
        if (!e.Args.Contains("--demo"))
        {
            instance = new Mutex(true, @"Local\PocketQuiet.UI.v2", out ownsInstance);
            if (!ownsInstance)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", Activation, PipeDirection.Out);
                    // Connecting is the activation signal; the server does not read a payload.
                    pipe.Connect(1500);
                }
                // Activation is best effort. A second instance must never stay alive in a modal dialog.
                catch (IOException) { }
                catch (TimeoutException) { }
                catch (UnauthorizedAccessException) { }
                Shutdown(); return;
            }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(args.Exception.Message, "Pocket Quiet");
            args.Handled = true;
        };
        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
        if (ownsInstance)
        {
            activationStop = new();
            _ = ListenForActivation(window, activationStop.Token);
        }
    }
    async void RunMaintenance(string task)
    {
        int result = 0;
        try { System.Windows.MessageBox.Show(await ScriptRunner.Run(task), "Pocket Quiet"); }
        catch (Exception ex) { result = 1; System.Windows.MessageBox.Show(ex.Message, "Pocket Quiet · 작업 실패"); }
        Shutdown(result);
    }
    async Task ListenForActivation(MainWindow window, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Activation, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                await Dispatcher.InvokeAsync(window.Reveal);
            }
            catch (OperationCanceledException) { return; }
            catch { try { await Task.Delay(500, token); } catch (OperationCanceledException) { return; } }
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activationStop?.Cancel(); activationStop?.Dispose();
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
