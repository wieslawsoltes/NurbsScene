using System.Text.Json;
using NurbsScene.Kernel;
using NurbsScene.Parametric;
var tests = new List<(string Name, Action Run)>();
PanelScrollingContracts.Register(tests, StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
var failed = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
Console.WriteLine(JsonSerializer.Serialize(new { suite = "panel-scrolling", total = tests.Count, passed = tests.Count - failed, failed }));
if (failed == 0 && args.Length > 0) PanelScrollPerformance.Run(args[0]);
return failed == 0 ? 0 : 1;
