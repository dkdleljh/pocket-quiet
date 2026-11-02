using PocketQuiet.Core;
int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
var now = DateTimeOffset.UtcNow; var adaptive = new Settings { Mode = Preset.Adaptive };
Sample S(double? t = 50, int? rpm = 2200, int offset = 0) => new(now.AddSeconds(offset), t, rpm, 20, 10);
var e = new ControlEngine();
Check(e.Step(S(), new(), now).Automatic, "Firmware never commands manual output");
foreach (double? value in new double?[] { null, double.NaN, double.PositiveInfinity, -1, 116 }) { e.Reset(); Check(e.Step(S(value), adaptive, now).Fault, "Invalid temperature returns to firmware: " + value); }
e.Reset(); Check(e.Step(S(), adaptive, now.AddSeconds(4)).Fault, "Stale sample rejected");
e.Reset(); Check(e.Step(S(offset: 5), adaptive, now).Fault, "Future sample rejected");
e.Reset(); e.Step(S(), adaptive, now); Check(e.Step(S(), adaptive, now).Fault, "Repeated timestamp rejected");
foreach (int? rpm in new int?[] { null, -1, 12001 }) { e.Reset(); Check(e.Step(S(rpm: rpm), adaptive, now).Fault, "Invalid RPM rejected: " + rpm); }
e.Reset(); e.Step(S(rpm: 0), adaptive, now); Check(e.Step(S(rpm: 0, offset: 1), adaptive, now.AddSeconds(1)).Output == 100, "Stall recovery uses maximum cooling");
Decision? d = null; for (int i = 2; i < 5; i++) d = e.Step(S(rpm: 0, offset: i), adaptive, now.AddSeconds(i)); Check(d!.Fault && d.Automatic, "Persistent stall releases control");
foreach (var mode in Enum.GetValues<Preset>().Where(x => x != Preset.Firmware)) { e.Reset(); Check(e.Step(S(85), new Settings { Mode = mode }, now).Output == 100, "High-temperature override: " + mode); }
e.Reset(); for (int i = 0; i < 15; i++) { d = e.Step(S(40, offset: i), adaptive, now.AddSeconds(i)); }
Check(d!.Output == 60, "Short cool period does not trigger rapid deceleration");
for (int i = 15; i < 90; i++) d = e.Step(S(40, offset: i), adaptive, now.AddSeconds(i)); Check(d!.Output is >= 30 and < 40, "Sustained cool period reduces output with floor");
e.Reset(); for (int i = 0; i < 100; i++) e.Step(S(40, offset: i), adaptive, now.AddSeconds(i)); Check(e.Step(S(85, offset: 100), adaptive, now.AddSeconds(100)).Output == 100, "Emergency bypasses smoothing and rate limits");
Check(ControlEngine.Interpolate([new(40, 30), new(60, 50)], 50) == 40, "Curve interpolation");
foreach (var invalid in new Settings[] { new() { Preference = double.NaN }, new() { Mode = (Preset)99 }, new() { Curve = [] }, new() { Curve = [new(40, 20), new(85, 100)] }, new() { Curve = [new(40, 60), new(60, 40), new(85, 100)] }, new() { Curve = [new(40, 30), new(80, 100)] } }) { bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Unsafe settings rejected"); }
var random = new Random(19); bool bounded = true; foreach (var mode in Enum.GetValues<Preset>().Where(x => x != Preset.Firmware)) { e.Reset(); for (int i = 0; i < 10000; i++) { double t = 20 + random.NextDouble() * 85; d = e.Step(S(t, offset: i), new Settings { Mode = mode, Preference = random.NextDouble() * 2 - 1 }, now.AddSeconds(i)); bounded &= !d.Automatic && d.Output >= 30 && d.Output <= 100 && (t < 85 || d.Output == 100); } }
Check(bounded, "60,000 randomized decisions preserve safety bounds");
var quiet = new ControlEngine(); var cool = new ControlEngine(); double q = 0, c = 0; for (int i = 0; i < 120; i++) { q = quiet.Step(S(60, offset: i), new Settings { Mode = Preset.Quiet }, now.AddSeconds(i)).Output!.Value; c = cool.Step(S(60, offset: i), new Settings { Mode = Preset.Cooling }, now.AddSeconds(i)).Output!.Value; }
Check(q < c, "Cooling preset runs faster than quiet at same temperature");
await RegressionTests.Run(Check);
Console.WriteLine($"ALL {count} CHECKS PASSED");
