using Path = System.IO.Path;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PocketQuiet.Core;
using Forms = System.Windows.Forms;
namespace PocketQuiet.App;

public partial class MainWindow : Window
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<double?> history = []; readonly List<(TextBox T, TextBox P)> fields = [];
    readonly Forms.NotifyIcon tray;
    readonly System.Drawing.Icon trayIcon; readonly bool demo; readonly string[] args; bool polling, quitting, initialized;
    Status? latest; readonly SemaphoreSlim requests = new(1, 1);
    readonly string[] labels = ["기기 기본 자동", "AI 자동 · 적응형", "저소음", "균형", "냉각 우선", "사용자 지정"];
    public MainWindow(string[] args)
    {
        this.args = args; demo = args.Contains("--demo"); InitializeComponent();
        var area = SystemParameters.WorkArea; MinWidth = Math.Min(850, area.Width - 32); MinHeight = Math.Min(580, area.Height - 32); Width = Math.Min(1050, area.Width - 32); Height = Math.Min(850, area.Height - 32); Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2; WindowStartupLocation = WindowStartupLocation.Manual;
        ModeBox.ItemsSource = labels; ModeBox.SelectedIndex = 0;
        foreach (var point in new Settings().Curve) { var stack = new StackPanel(); var t = new TextBox { Text = point.Temperature.ToString(CultureInfo.InvariantCulture) }; var p = new TextBox { Text = point.Output.ToString(CultureInfo.InvariantCulture) }; stack.Children.Add(t); stack.Children.Add(p); CurveFields.Children.Add(stack); fields.Add((t, p)); }
        using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("PocketQuiet.TrayIcon")
            ?? throw new InvalidOperationException("Pocket Quiet 아이콘 리소스를 찾을 수 없습니다."))
        using (var loadedIcon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize))
            trayIcon = (System.Drawing.Icon)loadedIcon.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Pocket Quiet", Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Pocket Quiet 열기", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("기기 기본 자동으로 복귀", null, async (_, _) => await RestoreAsync()); menu.Items.Add("자동 복귀 후 종료", null, async (_, _) => { if (await RestoreAsync()) { quitting = true; Close(); } }); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } else { timer.Stop(); tray.Dispose(); trayIcon.Dispose(); } };
        System.Windows.Application.Current.SessionEnding += (_, _) => { quitting = true; };
        Loaded += async (_, _) => { await Poll(); timer.Tick += async (_, _) => await Poll(); timer.Start(); };
    }
    async Task<Response> Send(Request request)
    {
        await requests.WaitAsync(); try
        {
            using var timeout = new CancellationTokenSource(3500); using var pipe = new NamedPipeClientStream(".", Wire.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous); await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true }; using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, Wire.Json).AsMemory(), timeout.Token); var line = await reader.ReadLineAsync(timeout.Token);
            return JsonSerializer.Deserialize<Response>(line ?? "", Wire.Json) ?? throw new IOException("서비스 응답이 없습니다.");
        }
        finally { requests.Release(); }
    }
    async Task Poll()
    {
        if (polling) return; polling = true; try
        {
            Status s; if (demo) { double t = 52 + Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds / 12) * 8; s = new() { Device = "데모 · 실제 하드웨어 제어 없음", Bios = "DEMO", Ready = false, Temperature = t, Rpm = 2400, CpuLoad = 22, Power = 12, Reason = "화면 미리보기 · 센서 값은 시뮬레이션입니다." }; }
            else { s = (await Send(new("status"))).Status; if ((DateTimeOffset.UtcNow - s.Time).TotalSeconds > 4) throw new IOException("센서 갱신이 지연되고 있습니다. 표시 값은 최신이 아닙니다."); }
            latest = s; DeviceLabel.Text = $"{s.Device} · BIOS {s.Bios}"; ConnectionLabel.Text = demo ? "데모" : s.Ready ? "연결됨" : "확인 필요";
            TemperatureLabel.Text = s.Temperature is double t2 ? $"{t2:F1} °C" : "— °C"; RpmLabel.Text = s.Rpm is int rpm ? $"{rpm:N0}" : "— RPM";
            DutyLabel.Text = s.Output is double duty ? $"출력 {duty:F0}% · RPM 실측" : "RPM · 기기 기본 자동"; LoadLabel.Text = s.CpuLoad is double load ? $"{load:F0} %" : "— %"; PowerLabel.Text = s.Power is double power ? $"전력: {power:F1} W" : "전력: 센서 미지원";
            ReasonLabel.Text = $"{labels[(int)s.ActiveMode]} / {s.Reason}"; ApplyButton.IsEnabled = !demo && (s.Ready || ModeBox.SelectedIndex == 0);
            AlertBox.Visibility = s.Error is null ? Visibility.Collapsed : Visibility.Visible; AlertLabel.Text = s.Error;
            tray.Text = $"Pocket Quiet · {s.Temperature:F0}°C · {s.Rpm} RPM";
            history.Add(s.Temperature); if (history.Count > 120) history.RemoveAt(0); DrawChart();
            if (!initialized) { initialized = true; SetFields(s.Settings); }
            if (args.Contains("--screenshot") && history.Count == 12) { int i = Array.IndexOf(args, "--screenshot"); SaveScreenshot(args[i + 1]); if (args.Contains("--exit-after-screenshot")) { quitting = true; Close(); } }
        }
        catch (Exception ex) { ConnectionLabel.Text = "연결 끊김"; TemperatureLabel.Text = "— °C"; RpmLabel.Text = "— RPM"; LoadLabel.Text = "— %"; DutyLabel.Text = "제어 상태 확인 불가"; PowerLabel.Text = "전력: —"; ReasonLabel.Text = "서비스 연결을 확인하세요."; ApplyButton.IsEnabled = false; AlertBox.Visibility = Visibility.Visible; AlertLabel.Text = "Pocket Quiet 서비스를 시작하거나 설치 스크립트를 실행하세요. " + ex.Message; }
        finally { polling = false; }
    }
    void SetFields(Settings s) { ModeBox.SelectedIndex = (int)s.Mode; Preference.Value = s.Preference; for (int i = 0; i < Math.Min(fields.Count, s.Curve.Length); i++) { fields[i].T.Text = s.Curve[i].Temperature.ToString(CultureInfo.InvariantCulture); fields[i].P.Text = s.Curve[i].Output.ToString(CultureInfo.InvariantCulture); } }
    Settings ReadSettings() { var s = new Settings { Mode = (Preset)ModeBox.SelectedIndex, Preference = Preference.Value, Curve = fields.Select(f => new CurvePoint(double.Parse(f.T.Text, CultureInfo.InvariantCulture), double.Parse(f.P.Text, CultureInfo.InvariantCulture))).ToArray() }; s.Validate(); return s; }
    async void Apply(object sender, RoutedEventArgs e) { try { var response = await Send(new("apply", ReadSettings())); if (!response.Ok) throw new InvalidOperationException(response.Error); Footer.Text = "설정을 적용했습니다. 실제 상태는 다음 측정에 반영됩니다."; await Poll(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "설정 적용 실패"); } }
    async Task<bool> RestoreAsync() { if (demo) return true; try { var response = await Send(new("apply", new Settings { Mode = Preset.Firmware, Preference = Preference.Value, Curve = latest?.Settings.Curve ?? new Settings().Curve })); if (!response.Ok) throw new IOException(response.Error); ModeBox.SelectedIndex = 0; await Poll(); return true; } catch (Exception ex) { System.Windows.MessageBox.Show("기본 자동 복귀를 확인하지 못했습니다. " + ex.Message, "Pocket Quiet"); return false; } }
    async void Restore(object sender, RoutedEventArgs e) => await RestoreAsync();
    async void Reset(object sender, RoutedEventArgs e) { try { var response = await Send(new("reset")); if (!response.Ok) throw new IOException(response.Error); SetFields(new()); Footer.Text = "기본 자동 모드와 기본 설정으로 초기화했습니다."; await Poll(); } catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "초기화 실패"); } }
    void ModeChanged(object sender, SelectionChangedEventArgs e) { if (AdaptivePanel is null) return; AdaptivePanel.Visibility = ModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; CurvePanel.Visibility = ModeBox.SelectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed; ModeDescription.Text = ModeBox.SelectedIndex switch { 0 => "기기 펌웨어가 팬을 제어합니다. 앱의 곡선은 적용되지 않습니다.", 1 => "온도 추세를 분석해 빠르게 가속하고 천천히 감속합니다. 로컬 규칙 기반 적응형 제어이며 학습형 AI 모델은 아닙니다.", 2 => "가벼운 작업에서 소음을 줄입니다. 고온에서는 보호 규칙이 우선합니다.", 3 => "온도와 소음 사이의 균형을 유지합니다.", 4 => "열이 쌓이기 전에 팬을 높여 냉각을 우선합니다.", _ => "온도와 출력을 증가 순서로 입력하세요. 최소 출력은 30%입니다." }; }
    void DrawChart()
    {
        int width = Math.Max(1, (int)(Chart.ActualWidth > 0 ? Chart.ActualWidth : 760));
        const int height = 110;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 41, 46)), null, new Rect(0, 0, width, height));
            foreach (int mark in new[] { 40, 60, 85 })
            {
                double y = height - mark / 110d * height;
                dc.DrawLine(new Pen(new SolidColorBrush(mark == 85 ? Color.FromRgb(104, 77, 54) : Color.FromRgb(53, 78, 82)), 1), new(0, y), new(width, y));
            }
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(142, 227, 176)), 2.5);
            System.Windows.Point? previous = null;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i] is not double t) { previous = null; continue; }
                var point = new System.Windows.Point(width * i / 119d, height - Math.Clamp(t, 0, 110));
                if (previous is System.Windows.Point last) dc.DrawLine(pen, last, point);
                else dc.DrawEllipse(pen.Brush, null, point, 2, 2);
                previous = point;
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); Chart.Source = bitmap;
    }
    void HideToTray(object sender, RoutedEventArgs e) => Hide();
    void OpenLogs(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PocketQuiet")) { UseShellExecute = true });
    void OpenManual(object sender, RoutedEventArgs e) { string path = Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.ko.html"); if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else System.Windows.MessageBox.Show("바탕화면 Pocket Quiet 폴더의 사용설명서를 확인하세요."); }
    void SaveScreenshot(string path) { File.WriteAllText(path + ".diag", $"Chart={Chart.ActualWidth}x{Chart.ActualHeight}; Size={Chart.RenderSize}; Samples={history.Count}; Window={ActualWidth}x{ActualHeight}"); var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using var file = File.Create(path); encoder.Save(file); }
}
