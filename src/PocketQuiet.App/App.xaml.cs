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
        // Demo previews can run alongside the real UI and never send control requests.
        if (!e.Args.Contains("--demo"))
        {
            instance = new Mutex(true, @"Local\PocketQuiet.UI.v2", out ownsInstance);
            if (!ownsInstance)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", Activation, PipeDirection.Out);
                    pipe.Connect(1500); pipe.WriteByte(1);
                }
                catch { System.Windows.MessageBox.Show("Pocket Quiet가 이미 실행 중입니다. 트레이 아이콘을 열어주세요."); }
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
