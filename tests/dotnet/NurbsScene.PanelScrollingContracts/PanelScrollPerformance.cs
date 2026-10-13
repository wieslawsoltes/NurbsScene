using System.Diagnostics;
using System.Text.Json;
using NurbsScene.Parametric;
using NurbsScene.Presentation;
internal static class PanelScrollPerformance
{
    public static void Run(string path)
    {
        var tree = new DataTree(Enumerable.Range(0, 100000).Select(i => new DataBranch(new([i]), [(object?)i])));
        var index = new PanelRecordIndex(tree); long checksum = 0;
        PanelRecord Linear(int offset) { for (var b = 0; b < tree.Branches.Length; b++) { if (offset < tree.Branches[b].Items.Length) return new(b, offset); offset -= tree.Branches[b].Items.Length; } throw new Exception(); }
        object Samples(Func<int, PanelRecord> locate)
        {
            for (var i = 0; i < 100; i++) checksum += locate(99000 + i % 500).Branch;
            var times = new double[7]; var bytes = new long[7];
            for (var sample = 0; sample < 7; sample++) {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                for (var i = 0; i < 1000; i++) { var record = locate(99000 + i % 500); checksum += record.Branch + record.Item; }
                times[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / 1000;
                bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 1000;
            }
            return new { microseconds = times, bytesPerOperation = bytes };
        }
        var linear = Samples(Linear); var indexed = Samples(offset => index.Locate(offset, false));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { scope = "same-process immutable 100000-branch item lookup, not rendering or application FPS", runtime = Environment.Version.ToString(), linear, indexed, checksum }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
