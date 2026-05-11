using ScottPlot;
using System.Globalization;

var repoRoot = FindRepoRoot();
var experimentsDir = Path.Combine(repoRoot, "password-break-server", "experiments");

if (!Directory.Exists(experimentsDir))
{
    Console.WriteLine($"Experiments folder not found: {experimentsDir}");
    return;
}

var outputDir = Path.Combine(experimentsDir, "_plots_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(outputDir);

var rows = Directory
    .GetFiles(experimentsDir, "metrics.csv", SearchOption.AllDirectories)
    .SelectMany(ReadMetrics)
    .Where(r => r.TaskSentAtUtc != DateTime.MinValue)
    .Where(r => r.ResultReceivedAtUtc != DateTime.MinValue)
    .Where(r => r.CandidatesCount > 0)
    .ToList();

if (rows.Count == 0)
{
    Console.WriteLine("No metrics.csv rows found.");
    return;
}

var runs = BuildRunSummaries(rows);

SaveThroughputOverTime(rows, outputDir);
SaveTotalThroughputByClients(runs, outputDir);
SaveTotalThroughputByThreads(runs, outputDir);
SaveTotalThroughputByChunkSize(runs, outputDir);
SaveRunSummaryThroughput(runs, outputDir);

Console.WriteLine("Plots saved to:");
Console.WriteLine(outputDir);

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);

    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "password-break-server")))
            return dir.FullName;

        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static List<MetricRow> ReadMetrics(string path)
{
    var result = new List<MetricRow>();
    var lines = File.ReadAllLines(path);

    if (lines.Length <= 1)
        return result;

    var headers = lines[0].Split(',');
    var index = headers
        .Select((name, i) => new { name = name.Trim(), i })
        .ToDictionary(x => x.name, x => x.i);

    for (var i = 1; i < lines.Length; i++)
    {
        if (string.IsNullOrWhiteSpace(lines[i]))
            continue;

        var c = lines[i].Split(',');

        result.Add(new MetricRow
        {
            SourceFile = path,
            RunId = Get(c, index, "run_id"),
            ExperimentName = Get(c, index, "experiment_name"),
            RunSequence = GetInt(c, index, "run_sequence"),
            AttackMode = Get(c, index, "attack_mode"),
            ChunkSize = GetLong(c, index, "chunk_size"),
            ClientThreads = GetInt(c, index, "client_threads"),
            ConnectedClientsAtStart = GetInt(c, index, "connected_clients_at_start"),
            ConnectedClientsAtResult = GetInt(c, index, "connected_clients_at_result"),
            TaskId = Get(c, index, "task_id"),
            ClientId = Get(c, index, "client_id"),
            StartIndex = GetLong(c, index, "start_index"),
            EndIndex = GetLong(c, index, "end_index"),
            CandidatesCount = GetLong(c, index, "candidates_count"),
            ComputeTimeMs = GetLong(c, index, "compute_time_ms"),
            TotalTimeMs = GetLong(c, index, "total_time_ms"),
            Throughput = GetDouble(c, index, "throughput_candidates_per_sec"),
            FoundCount = GetInt(c, index, "found_count"),
            TaskSentAtUtc = GetDate(c, index, "task_sent_at_utc"),
            ResultReceivedAtUtc = GetDate(c, index, "result_received_at_utc")
        });
    }

    return result;
}

static List<RunSummary> BuildRunSummaries(List<MetricRow> rows)
{
    return rows
        .GroupBy(r => r.ExperimentName)
        .Select(g =>
        {
            var ordered = g.OrderBy(r => r.TaskSentAtUtc).ToList();

            var start = ordered.Min(r => r.TaskSentAtUtc);
            var end = ordered.Max(r => r.ResultReceivedAtUtc);
            var seconds = Math.Max(0.001, (end - start).TotalSeconds);
            var candidates = ordered.Sum(r => r.CandidatesCount);

            return new RunSummary
            {
                ExperimentName = g.Key,
                RunSequence = ordered.Min(r => r.RunSequence),
                ChunkSize = ordered.First().ChunkSize,
                ClientThreads = ordered.First().ClientThreads,
                ConnectedClientsAtStart = ordered.Max(r => r.ConnectedClientsAtStart),
                CandidatesCount = candidates,
                DurationSeconds = seconds,
                TotalThroughput = candidates / seconds
            };
        })
        .OrderBy(r => r.RunSequence)
        .ThenBy(r => r.ExperimentName)
        .ToList();
}

static void SaveThroughputOverTime(List<MetricRow> rows, string outputDir)
{
    var plot = new Plot();

    foreach (var run in rows.GroupBy(r => r.ExperimentName).OrderBy(g => g.Key))
    {
        var ordered = run.OrderBy(r => r.ResultReceivedAtUtc).ToList();

        if (ordered.Count < 2)
            continue;

        var start = ordered.Min(r => r.ResultReceivedAtUtc);

        var xs = ordered
            .Select(r => (r.ResultReceivedAtUtc - start).TotalSeconds)
            .ToArray();

        var ys = ordered
            .Select(r => r.Throughput)
            .ToArray();

        plot.Add.Scatter(xs, ys).LegendText = run.Key;
    }

    plot.Title("Task throughput over time");
    plot.XLabel("Seconds from run start");
    plot.YLabel("Task candidates / second");
    plot.ShowLegend();

    plot.SavePng(Path.Combine(outputDir, "throughput_over_time.png"), 1600, 900);
}

static void SaveTotalThroughputByClients(List<RunSummary> runs, string outputDir)
{
    var points = runs
        .GroupBy(r => r.ConnectedClientsAtStart)
        .OrderBy(g => g.Key)
        .Select(g => new
        {
            X = (double)g.Key,
            Y = g.Average(r => r.TotalThroughput)
        })
        .ToList();

    SaveScatter(
        points.Select(p => p.X).ToArray(),
        points.Select(p => p.Y).ToArray(),
        "Total throughput vs clients",
        "Connected clients",
        "Total candidates / second",
        Path.Combine(outputDir, "total_throughput_by_clients.png"));
}

static void SaveTotalThroughputByThreads(List<RunSummary> runs, string outputDir)
{
    var points = runs
        .GroupBy(r => r.ClientThreads)
        .OrderBy(g => g.Key)
        .Select(g => new
        {
            X = (double)g.Key,
            Y = g.Average(r => r.TotalThroughput)
        })
        .ToList();

    SaveScatter(
        points.Select(p => p.X).ToArray(),
        points.Select(p => p.Y).ToArray(),
        "Total throughput vs client threads",
        "Client threads, 0 = all available",
        "Total candidates / second",
        Path.Combine(outputDir, "total_throughput_by_threads.png"));
}

static void SaveTotalThroughputByChunkSize(List<RunSummary> runs, string outputDir)
{
    var points = runs
        .GroupBy(r => r.ChunkSize)
        .OrderBy(g => g.Key)
        .Select(g => new
        {
            X = (double)g.Key,
            Y = g.Average(r => r.TotalThroughput)
        })
        .ToList();

    SaveScatter(
        points.Select(p => p.X).ToArray(),
        points.Select(p => p.Y).ToArray(),
        "Total throughput vs chunk size",
        "Chunk size",
        "Total candidates / second",
        Path.Combine(outputDir, "total_throughput_by_chunk_size.png"));
}

static void SaveRunSummaryThroughput(List<RunSummary> runs, string outputDir)
{
    var points = runs
        .Select((r, i) => new
        {
            X = (double)(i + 1),
            Y = r.TotalThroughput
        })
        .ToList();

    SaveScatter(
        points.Select(p => p.X).ToArray(),
        points.Select(p => p.Y).ToArray(),
        "Total run throughput",
        "Run number",
        "Total candidates / second",
        Path.Combine(outputDir, "run_summary_throughput.png"));
}

static void SaveScatter(
    double[] xs,
    double[] ys,
    string title,
    string xLabel,
    string yLabel,
    string path)
{
    var plot = new Plot();

    if (xs.Length > 0)
        plot.Add.Scatter(xs, ys);

    plot.Title(title);
    plot.XLabel(xLabel);
    plot.YLabel(yLabel);

    plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
    {
        MinimumTickSpacing = 80,
        IntegerTicksOnly = true
    };

    plot.SavePng(path, 1400, 800);
}

static string Get(string[] c, Dictionary<string, int> index, string name)
    => index.TryGetValue(name, out var i) && i < c.Length ? c[i] : "";

static int GetInt(string[] c, Dictionary<string, int> index, string name)
    => int.TryParse(Get(c, index, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

static long GetLong(string[] c, Dictionary<string, int> index, string name)
    => long.TryParse(Get(c, index, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

static double GetDouble(string[] c, Dictionary<string, int> index, string name)
    => double.TryParse(Get(c, index, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

static DateTime GetDate(string[] c, Dictionary<string, int> index, string name)
    => DateTime.TryParse(
        Get(c, index, name),
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal,
        out var v)
        ? v
        : DateTime.MinValue;

class MetricRow
{
    public string SourceFile { get; set; } = "";
    public string RunId { get; set; } = "";
    public string ExperimentName { get; set; } = "";
    public int RunSequence { get; set; }
    public string AttackMode { get; set; } = "";
    public long ChunkSize { get; set; }
    public int ClientThreads { get; set; }
    public int ConnectedClientsAtStart { get; set; }
    public int ConnectedClientsAtResult { get; set; }
    public string TaskId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public long StartIndex { get; set; }
    public long EndIndex { get; set; }
    public long CandidatesCount { get; set; }
    public long ComputeTimeMs { get; set; }
    public long TotalTimeMs { get; set; }
    public double Throughput { get; set; }
    public int FoundCount { get; set; }
    public DateTime TaskSentAtUtc { get; set; }
    public DateTime ResultReceivedAtUtc { get; set; }
}

class RunSummary
{
    public string ExperimentName { get; set; } = "";
    public int RunSequence { get; set; }
    public long ChunkSize { get; set; }
    public int ClientThreads { get; set; }
    public int ConnectedClientsAtStart { get; set; }
    public long CandidatesCount { get; set; }
    public double DurationSeconds { get; set; }
    public double TotalThroughput { get; set; }
}
