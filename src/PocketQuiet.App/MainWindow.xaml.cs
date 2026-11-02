using Path = System.IO.Path;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PocketQuiet.Core;
using Forms = System.Windows.Forms;

namespace PocketQuiet.App;

public partial class MainWindow : Window
{
    public record ModeOption(Preset Mode, string Label);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly List<(DateTimeOffset Time, double? Temperature)> history = [];
    readonly List<(TextBox Temperature, TextBox Output)> fields = [];
    readonly Forms.NotifyIcon tray;
    readonly System.Drawing.Icon trayIcon;
    readonly IAppClient client;
    readonly bool demo;
    readonly string[] args;
    bool polling, quitting, initialized, dirty, busy, screenshotSaved;
    bool updatingFields = true;
    Status? latest;
    DateTimeOffset? lastChartSample;
    Preset SelectedMode => (ModeBox.SelectedItem as ModeOption)?.Mode ?? Preset.Firmware;

    public MainWindow(string[] args)
    {
        this.args = args;
        demo = args.Contains("--demo");
        client = demo ? new DemoClient() : new ServiceClient();
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(820, area.Width - 32); MinHeight = Math.Min(580, area.Height - 32);
        Width = Math.Min(1080, area.Width - 32); Height = Math.Min(940, area.Height - 32);
        Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ModeBox.ItemsSource = Presets.DisplayOrder.Select(mode => new ModeOption(mode, Presets.Label(mode))).ToArray();
        SetFields(new());
        using (var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("PocketQuiet.TrayIcon")
            ?? throw new InvalidOperationException("아이콘 리소스를 찾을 수 없습니다."))
        using (var loaded = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            trayIcon = (System.Drawing.Icon)loaded.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = demo ? "Pocket Quiet 데모" : "Pocket Quiet", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Pocket Quiet 열기", null, (_, _) => Dispatcher.Invoke(Reveal));
        menu.Items.Add("기기 기본 자동으로 복귀", null, async (_, _) => await RestoreAsync());
        menu.Items.Add("자동 복귀 후 종료", null, async (_, _) => { if (await RestoreAsync()) { quitting = true; Close(); } });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
        Closing += (_, e) =>
        {
            if (!quitting) { e.Cancel = true; Hide(); }
            else { timer.Stop(); tray.Dispose(); trayIcon.Dispose(); }
        };
        System.Windows.Application.Current.SessionEnding += (_, _) => quitting = true;
        IsVisibleChanged += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromSeconds(IsVisible ? 1 : 5);
            if (IsLoaded && IsVisible) await Poll();
        };
        Loaded += async (_, _) =>
        {
            int preview = Array.IndexOf(args, "--preview-mode");
            if (demo && preview >= 0 && preview + 1 < args.Length && Enum.TryParse<Preset>(args[preview + 1], out var mode))
                await client.Send(new("apply", new Settings { Mode = mode }));
            await Poll();
            timer.Tick += async (_, _) => await Poll();
            timer.Start();
        };
    }

    public void Reveal() { Show(); WindowState = WindowState.Normal; Activate(); }

    async Task Poll()
    {
        if (polling || quitting) return;
        polling = true;
        try
        {
            var response = await client.Send(new("status"));
            if (!response.Ok) throw new IOException(response.Error);
            var s = response.Status;
            if ((DateTimeOffset.UtcNow - s.Time).TotalSeconds > 4 || s.Time > DateTimeOffset.UtcNow.AddSeconds(1))
                throw new IOException("제어 서비스 갱신이 지연되고 있습니다.");
            latest = s;
            DeviceLabel.Text = $"{s.Device} · BIOS {s.Bios} · 서비스 {s.Version}";
            ConnectionLabel.Text = demo ? "데모" : s.Suspended ? "절전" : s.Ready ? "연결됨" : "확인 필요";
            bool fresh = s.SampleTime is DateTimeOffset measured && (DateTimeOffset.UtcNow - measured).TotalSeconds is >= -1 and <= 4;
            TemperatureLabel.Text = fresh && s.Temperature is double t ? $"{t:F1} °C" : "— °C";
            RpmLabel.Text = fresh && s.Rpm is int rpm ? $"{rpm:N0}" : "— RPM";
            LoadLabel.Text = fresh && s.CpuLoad is double load ? $"{load:F0} %" : "— %";
            PowerLabel.Text = fresh && s.Power is double power ? $"전력: {power:F1} W" : "전력: —";
            DutyLabel.Text = s.ControlState == ControlState.Manual && s.Output is double output ? $"요청 출력 {output:F0}%" : s.ControlState == ControlState.Firmware ? "펌웨어 자동 · 복귀 확인" : "제어 상태 미확인";
            ActiveLabel.Text = s.ControlState == ControlState.Unknown ? "적용 상태: 확인할 수 없음" : $"적용 중: {Presets.Label(s.ActiveMode)}";
            if (s.Pending) ActiveLabel.Text += $" → {Presets.Label(s.Settings.Mode)} 적용 대기";
            ReasonLabel.Text = s.Reason;
            SampleLabel.Text = s.SampleTime is DateTimeOffset time
                ? $"마지막 정상 측정: {time.ToLocalTime():HH:mm:ss} · {Math.Max(0, (DateTimeOffset.UtcNow - time).TotalSeconds):F0}초 전 · 복구 감시 {(s.WatchdogHealthy ? "정상" : "미확인")}" : "정상 센서 측정 대기 중";
            var warnings = new[] { s.Error, s.PersistenceWarning, Compatible(s) ? null : "서비스 업데이트가 필요합니다. Install.cmd를 실행하세요." }.Where(text => !string.IsNullOrWhiteSpace(text));
            AlertLabel.Text = string.Join("\n", warnings);
            AlertBox.Visibility = AlertLabel.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            ProtectionBox.Visibility = s.LastProtection is null ? Visibility.Collapsed : Visibility.Visible;
            if (s.LastProtection is { } protection)
                ProtectionLabel.Text = $"최근 보호 기록 · {protection.Time.ToLocalTime():MM/dd HH:mm:ss}\n{protection.Message}";
            NoiseLabel.Text = s.Temperature is >= 80
                ? "현재 고온 보호로 팬 출력을 높이고 있습니다. 무소음 모드도 냉각을 우선합니다. 지속적인 큰 바람 소리는 전력 효율 설정과 작업 부하·흡배기 상태를 함께 확인하세요."
                : s.Power is >= 20
                    ? "패키지 전력이 높게 측정되고 있습니다. 전력 효율 설정과 백그라운드 작업을 확인하면 발열과 팬 소음을 줄이는 데 도움이 될 수 있습니다."
                    : "무소음 프리셋은 55°C 이하에서 최소 30% 출력을 목표로 합니다. 완전한 팬 정지·0dB는 보장하지 않으며, 감속에는 대기 시간이 있습니다.";
            tray.Text = fresh && s.Temperature is double value ? $"Pocket Quiet · {value:F0}°C · {s.Rpm} RPM" : "Pocket Quiet · 센서 상태 확인 필요";
            if (fresh && s.Temperature is not null && s.SampleTime != lastChartSample)
            {
                history.Add((s.SampleTime!.Value, s.Temperature)); lastChartSample = s.SampleTime;
            }
            else if (!fresh || s.Temperature is null) history.Add((DateTimeOffset.UtcNow, null));
            if (!initialized || (!dirty && !busy)) { initialized = true; SetFields(s.Settings); }
            UpdateControls(); DrawChart();
            int capture = Array.IndexOf(args, "--screenshot");
            if (!screenshotSaved && capture >= 0 && capture + 1 < args.Length && history.Count >= 12)
            {
                screenshotSaved = true; SaveScreenshot(args[capture + 1]);
                if (demo && args.Contains("--exit-after-screenshot")) { quitting = true; Close(); }
            }
        }
        catch (Exception ex)
        {
            latest = null;
            ConnectionLabel.Text = "연결 끊김";
            TemperatureLabel.Text = "— °C"; RpmLabel.Text = "— RPM"; LoadLabel.Text = "— %";
            DutyLabel.Text = "제어 상태 미확인"; PowerLabel.Text = "전력: —";
            ActiveLabel.Text = "적용 상태: 확인할 수 없음";
            ReasonLabel.Text = "서비스 연결을 확인하세요.";
            SampleLabel.Text = "실시간 측정값을 받지 못했습니다.";
            tray.Text = "Pocket Quiet · 서비스 연결 끊김";
            AlertBox.Visibility = Visibility.Visible; AlertLabel.Text = ex.Message;
            history.Add((DateTimeOffset.UtcNow, null)); UpdateControls(); DrawChart();
        }
        finally { polling = false; }
    }

    static bool Compatible(Status status) => Version.TryParse(status.Version, out var version) && version >= new Version(0, 2, 0) && status.ProtocolVersion >= 2;

    void SetFields(Settings settings)
    {
        updatingFields = true;
        ModeBox.SelectedItem = ((ModeOption[])ModeBox.ItemsSource).FirstOrDefault(option => option.Mode == settings.Mode);
        Preference.Value = settings.Preference;
        if (fields.Count != settings.Curve.Length || fields.Where((field, index) => field.Temperature.Text != settings.Curve[index].Temperature.ToString(CultureInfo.InvariantCulture) || field.Output.Text != settings.Curve[index].Output.ToString(CultureInfo.InvariantCulture)).Any())
            BuildFields(settings.Curve);
        dirty = false;
        updatingFields = false;
        UpdateMode(); UpdateControls(); DrawCurve();
    }
    void BuildFields(CurvePoint[] curve)
    {
        fields.Clear(); CurveFields.Children.Clear();
        for (int i = 0; i < curve.Length; i++)
        {
            var panel = new StackPanel { Width = 88 };
            var t = new TextBox { Text = curve[i].Temperature.ToString(CultureInfo.InvariantCulture), ToolTip = $"{i + 1}번째 온도 (20~85°C)" };
            var output = new TextBox { Text = curve[i].Output.ToString(CultureInfo.InvariantCulture), ToolTip = $"{i + 1}번째 출력 (30~100%)" };
            System.Windows.Automation.AutomationProperties.SetName(t, $"{i + 1}번째 곡선 온도");
            System.Windows.Automation.AutomationProperties.SetName(output, $"{i + 1}번째 곡선 출력");
            panel.Children.Add(t); panel.Children.Add(output); CurveFields.Children.Add(panel); fields.Add((t, output));
            t.TextChanged += (_, _) => Changed(); output.TextChanged += (_, _) => Changed();
        }
    }
    CurvePoint[] ReadCurve() => fields.Select((field, index) =>
    {
        if (!double.TryParse(field.Temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
            throw new ArgumentException($"{index + 1}번째 온도를 숫자로 입력하세요.");
        if (!double.TryParse(field.Output.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double output))
            throw new ArgumentException($"{index + 1}번째 출력을 숫자로 입력하세요.");
        return new CurvePoint(t, output);
    }).ToArray();
    Settings ReadSettings()
    {
        var settings = new Settings
        {
            Mode = SelectedMode, Preference = Preference.Value,
            Curve = SelectedMode == Preset.Custom ? ReadCurve() : latest?.Settings.Curve ?? Presets.Curve(Preset.Balanced)
        };
        settings.Validate(); return settings;
    }
    void Changed()
    {
        if (updatingFields) return;
        dirty = true; UpdateControls(); DrawCurve();
    }
    void UpdateControls()
    {
        if (ApplyButton is null || ValidationLabel is null) return;
        bool valid = true;
        ValidationLabel.Text = "";
        foreach (var field in fields)
        {
            field.Temperature.BorderBrush = ValidNumber(field.Temperature.Text, 20, 85) ? new SolidColorBrush(Color.FromRgb(56, 81, 87)) : Brushes.Salmon;
            field.Output.BorderBrush = ValidNumber(field.Output.Text, 30, 100) ? new SolidColorBrush(Color.FromRgb(56, 81, 87)) : Brushes.Salmon;
        }
        try { ReadSettings(); }
        catch (Exception ex) { valid = false; ValidationLabel.Text = ex.Message; }
        DraftLabel.Text = dirty ? "미적용 변경사항 · 프리셋 적용을 눌러 반영하세요." : "표시된 설정은 서비스에 저장된 설정입니다.";
        ApplyButton.IsEnabled = !busy && valid && latest is not null && Compatible(latest) && (latest.Ready || SelectedMode == Preset.Firmware);
        RestoreButton.IsEnabled = ResetButton.IsEnabled = RetryButton.IsEnabled = !busy;
        AddPointButton.IsEnabled = fields.Count < 12; RemovePointButton.IsEnabled = fields.Count > 2;
    }
    static bool ValidNumber(string text, double min, double max) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && value >= min && value <= max;
    void ModeChanged(object sender, SelectionChangedEventArgs e) { if (ModeDescription is null) return; UpdateMode(); Changed(); }
    void PreferenceChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ValidationLabel is not null) Changed(); }
    void UpdateMode()
    {
        AdaptivePanel.Visibility = SelectedMode == Preset.Adaptive ? Visibility.Visible : Visibility.Collapsed;
        CurvePanel.Visibility = SelectedMode == Preset.Custom ? Visibility.Visible : Visibility.Collapsed;
        CurvePreview.Visibility = SelectedMode == Preset.Firmware ? Visibility.Collapsed : Visibility.Visible;
        ModeDescription.Text = SelectedMode switch
        {
            Preset.Firmware => "기기 펌웨어가 팬을 제어합니다. 앱의 곡선은 적용되지 않습니다.",
            Preset.Silent => "55°C 이하에서 30% 목표 출력을 유지합니다. 무소음 지향 프리셋이며 팬 정지·0dB를 보장하지 않습니다. 75/80/85°C 보호가 항상 우선합니다.",
            Preset.Adaptive => "온도 평균과 상승 속도로 냉각을 앞당깁니다. 인터넷·학습 모델을 사용하지 않는 로컬 규칙 기반 제어입니다.",
            Preset.Quiet => "가벼운 작업에서 팬 출력을 낮게 유지합니다. 고온에서는 냉각을 우선합니다.",
            Preset.Balanced => "온도와 소음 사이의 균형을 유지합니다.",
            Preset.Cooling => "열이 쌓이기 전에 팬을 높여 냉각을 우선합니다.",
            _ => "2~12개 점을 편집할 수 있습니다. 온도는 증가하고 출력은 감소하지 않아야 합니다."
        };
    }
    async Task<Response?> Execute(Request request)
    {
        if (busy) return null;
        busy = true; UpdateControls();
        try
        {
            var response = await client.Send(request);
            latest = response.Status;
            if (!response.Ok) throw new IOException(response.Error);
            Footer.Text = response.Status.PersistenceWarning ?? (demo ? "데모 설정만 변경했습니다. 실제 서비스에는 전달하지 않습니다." : "요청을 처리했습니다. 적용 상태를 확인하세요.");
            return response;
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Pocket Quiet"); return null; }
        finally { busy = false; UpdateControls(); }
    }
    async void Apply(object sender, RoutedEventArgs e)
    {
        try
        {
            var response = await Execute(new("apply", ReadSettings()));
            if (response is not null) { dirty = false; SetFields(response.Status.Settings); }
            await Poll();
        }
        catch (Exception ex) { ValidationLabel.Text = ex.Message; }
    }
    async Task<bool> RestoreAsync()
    {
        var requested = (latest?.Settings ?? new Settings()) with { Mode = Preset.Firmware };
        var response = await Execute(new("apply", requested));
        if (response is null) return false;
        if (response.Status.ControlState != ControlState.Firmware)
        {
            System.Windows.MessageBox.Show("기기 기본 자동 복귀를 확인하지 못했습니다. 서비스 업데이트와 기기 상태를 확인하세요.");
            return false;
        }
        SetFields(response.Status.Settings); await Poll(); return true;
    }
    async void Restore(object sender, RoutedEventArgs e) => await RestoreAsync();
    async void Reset(object sender, RoutedEventArgs e) { var response = await Execute(new("reset")); if (response is not null) SetFields(response.Status.Settings); await Poll(); }
    async void Retry(object sender, RoutedEventArgs e) { var response = await Execute(new("retry")); if (response is not null) SetFields(response.Status.Settings); await Poll(); }
    async void Acknowledge(object sender, RoutedEventArgs e) { await Execute(new("acknowledge")); await Poll(); }
    void AddPoint(object sender, RoutedEventArgs e)
    {
        try
        {
            var curve = ReadCurve().ToList(); new Settings { Curve = curve.ToArray() }.Validate();
            if (curve.Count >= 12) return;
            int gap = Enumerable.Range(0, curve.Count - 1).MaxBy(index => curve[index + 1].Temperature - curve[index].Temperature);
            var a = curve[gap]; var b = curve[gap + 1];
            curve.Insert(gap + 1, new((a.Temperature + b.Temperature) / 2, (a.Output + b.Output) / 2));
            BuildFields(curve.ToArray()); Changed();
        }
        catch (Exception ex) { ValidationLabel.Text = ex.Message; }
    }
    void RemovePoint(object sender, RoutedEventArgs e)
    {
        if (fields.Count <= 2) return;
        try { var curve = ReadCurve().ToList(); curve.RemoveAt(curve.Count - 2); BuildFields(curve.ToArray()); Changed(); }
        catch (Exception ex) { ValidationLabel.Text = ex.Message; }
    }
    void DefaultCurve(object sender, RoutedEventArgs e) { BuildFields(Presets.Curve(Preset.Balanced)); Changed(); }
    void ChartSizeChanged(object sender, SizeChangedEventArgs e) { if (Chart is not null) DrawChart(); }
    void CurveSizeChanged(object sender, SizeChangedEventArgs e) { if (CurvePreview is not null) DrawCurve(); }
    void DrawChart()
    {
        var now = DateTimeOffset.UtcNow;
        history.RemoveAll(point => point.Time < now.AddSeconds(-120));
        if (IsVisible) Chart.Source = Charts.History(Math.Max(200, Chart.ActualWidth), history, now);
    }
    void DrawCurve()
    {
        if (CurvePreview is null || updatingFields || !IsVisible) return;
        try
        {
            var settings = ReadSettings();
            var curve = SelectedMode == Preset.Custom ? settings.Curve : Presets.Curve(SelectedMode);
            CurvePreview.Source = Charts.Curve(Math.Max(200, CurvePreview.ActualWidth), curve);
        }
        catch { CurvePreview.Source = null; }
    }
    void HideToTray(object sender, RoutedEventArgs e) => Hide();
    static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "열기 실패"); }
    }
    void OpenLogs(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PocketQuiet");
        if (Directory.Exists(path)) OpenPath(path); else System.Windows.MessageBox.Show("아직 진단 기록이 없습니다.");
    }
    void OpenPowerSettings(object sender, RoutedEventArgs e) => OpenPath("ms-settings:powersleep");
    void OpenManual(object sender, RoutedEventArgs e) => OpenDocument("USER_GUIDE.ko.html");
    void OpenNoiseGuide(object sender, RoutedEventArgs e) => OpenDocument("NOISE.ko.html");
    void OpenDocument(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "docs", name);
        if (File.Exists(path)) OpenPath(path); else System.Windows.MessageBox.Show("배포 폴더의 문서를 확인하세요.");
    }
    void SaveScreenshot(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
