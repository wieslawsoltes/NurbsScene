using System.Text.Json;
using NurbsScene.Presentation;
using NurbsScene.Parametric;
using NurbsScene.Kernel;

var tests = new List<(string, Action)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Test("untouched native multiline drafts retain exact original line endings", () => {
    foreach (var original in new[] { "one\ntwo\n", "one\rtwo\r", "one\r\ntwo\r\n", "one\r\ntwo\nthree\rfour", "\r\n\n\r", "", "no newline" })
    {
        var draft = new PanelTextDraft(original);
        var native = original.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', '\r');
        Check(draft.Resolve(native) == original && draft.Resolve(original) == original);
    }
});
Test("changed logical text uses LF and preserves trailing empty rows", () => {
    var draft = new PanelTextDraft("old\r\nvalue");
    Check(draft.Resolve("new\r\ntext\r") == "new\ntext\n");
    Check(draft.Resolve("new\ntext\n") == "new\ntext\n");
    Check(draft.Resolve("a\u2028b") == "a\u2028b");
});
Test("empty edits and edit-then-revert preserve exact source semantics", () => {
    var original = "a\r\nb\n"; var draft = new PanelTextDraft(original);
    Check(draft.Resolve("") == ""); Check(draft.Resolve("changed") == "changed");
    Check(draft.Resolve("a\rb\r") == original);
});
Test("oversized text cannot be silently truncated by opening its editor", () => {
    Throws<ArgumentException>(() => new PanelTextDraft(new string('x', 65537)));
    var draft = new PanelTextDraft(""); Throws<ArgumentException>(() => draft.Resolve(new string('x', 65537)));
    Throws<ArgumentNullException>(() => draft.Resolve(null!));
});
Test("unchanged native coercion does not record a text edit or clear redo", () => {
    var graph = new Graph(StandardComponents.Create(new GeometryFactory(new ManagedNurbsEvaluator())));
    var node = graph.Add("Panel", parameters: new Dictionary<string, object?> { ["text"] = "a\nb\n" });
    var session = new GraphSession(graph); session.Edit("Move", g => g.Update(node with { X = 30 })); session.Undo();
    graph = session.Graph; node = graph.GetNode(node.Id); var revision = graph.Revision;
    var draft = new PanelTextDraft((string)node.Parameters["text"]!);
    Check(!CanvasValueEditing.SetText(session, graph, node, draft.Resolve("a\rb\r"), false));
    Check(graph.Revision == revision && session.CanRedo && graph.GetNode(node.Id).Parameters["text"] as string == "a\nb\n");
});
Test("warm exact draft reads allocate zero bytes", () => {
    const string text = "a\nb\n"; var draft = new PanelTextDraft(text);
    for (var i = 0; i < 100; i++) draft.Resolve(text);
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 10000; i++) draft.Resolve(text);
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(bytes == 0); Console.WriteLine($"METRIC exact-draft-10000={bytes} B");
});
var failed = 0;
foreach (var (name, run) in tests) try { run(); Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
Console.WriteLine(JsonSerializer.Serialize(new { suite = "text-drafts", total = tests.Count, passed = tests.Count - failed, failed })); return failed == 0 ? 0 : 1;
