using System.Windows;
namespace PocketQuiet.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); DispatcherUnhandledException += (_, a) => { System.Windows.MessageBox.Show(a.Exception.Message, "Pocket Quiet"); a.Handled = true; }; new MainWindow(e.Args).Show(); }
}
