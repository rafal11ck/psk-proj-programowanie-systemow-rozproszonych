using ScottPlot;
using System.Diagnostics;
using System.Globalization;

ConfigureFont();

var repoRoot = FindRepoRoot();
var experimentsDir = Path.Combine(repoRoot, "password-break-server", "experiments");

if (!Directory.Exists(experimentsDir))
{
    Console.WriteLine($"Experiments folder not found: {experimentsDir}");
    return;
}

// Stały katalog wyników - czyszczony przy każdym uruchomieniu, żeby nie zostawały stare wykresy.
var outputDir = Path.Combine(experimentsDir, "_plots");
if (Directory.Exists(outputDir))
    Directory.Delete(outputDir, recursive: true);
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

SaveThroughputByGranularityAndClients(runs, outputDir);
SaveGranularityPlotsPerClientCount(runs, outputDir);
SaveClientPlotsPerGranularity(runs, outputDir);

// Opracowanie wyników: skalowalność, narzut komunikacji, balans obciążenia.
SaveSpeedupByClients(runs, outputDir);
SaveEfficiencyByClients(runs, outputDir);
SaveComputeVsCommByChunk(runs, outputDir);
SaveCommOverheadByChunk(runs, outputDir);
SaveWorkPerSession(rows, outputDir);
SaveSessionChurnByClients(runs, outputDir);
SaveLoadImbalanceByClients(runs, outputDir);
SaveThroughputHeatmap(runs, outputDir);
SaveThroughputSurface(runs, outputDir);
SaveEfficiencySurface(runs, outputDir);

SaveRunSummaryCsv(runs, outputDir);

Console.WriteLine("Plots saved to:");
Console.WriteLine(outputDir);

// ScottPlot na Linuksie nie znajduje fontu systemowego (SkiaSharp bez fontconfig),
// więc ładujemy plik .ttf z dysku. Bold/italic osobno, bo tytuły i osie są bold.
static void ConfigureFont()
{
    if (OperatingSystem.IsWindows())
        return;

    var regular = ResolveFontFile("DejaVu Sans");

    if (regular == null)
    {
        Console.WriteLine("Warning: could not resolve a system font, plot text may be missing.");
        return;
    }

    try
    {
        ScottPlot.Fonts.AddFontFile("PlotFont", regular);

        TryAddFace("PlotFont", ResolveFontFile("DejaVu Sans:bold"), bold: true);
        TryAddFace("PlotFont", ResolveFontFile("DejaVu Sans:italic"), italic: true);
        TryAddFace("PlotFont", ResolveFontFile("DejaVu Sans:bold:italic"), bold: true, italic: true);

        ScottPlot.Fonts.Default = "PlotFont";
        Console.WriteLine($"Using font: {regular}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: failed to load font '{regular}': {ex.Message}");
    }
}

static void TryAddFace(string name, string? path, bool bold = false, bool italic = false)
{
    if (path != null)
        ScottPlot.Fonts.AddFontFile(name, path, bold, italic);
}

static string? ResolveFontFile(string pattern)
{
    // fontconfig zwraca ścieżkę pliku.
    var file = FcMatch(pattern);
    if (file != null && File.Exists(file))
        return file;

    foreach (var path in new[]
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf"
    })
    {
        if (File.Exists(path))
            return path;
    }

    return null;
}

static string? FcMatch(string pattern)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "fc-match",
            ArgumentList = { "--format=%{file}", pattern },
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null)
            return null;

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return string.IsNullOrWhiteSpace(output) ? null : output;
    }
    catch
    {
        return null;
    }
}

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
            CommunicationTimeMs = GetLong(c, index, "communication_time_ms"),
            TotalTimeMs = GetLong(c, index, "total_time_ms"),
            Throughput = GetDouble(c, index, "throughput_candidates_per_sec"),
            FoundCount = GetInt(c, index, "found_count"),
            ServerMachine = Get(c, index, "server_machine"),
            ConfiguredClients = GetInt(c, index, "configured_clients_count"),
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

            var clients = ordered.Max(r => Math.Max(
                r.ConnectedClientsAtStart,
                r.ConnectedClientsAtResult));

            var plannedClients = ParseClients(g.Key, clients);
            var chunkSize = ordered.First().ChunkSize;

            // Czas komunikacji per zadanie = total - compute (mierzone na serwerze).
            var avgCompute = ordered.Average(r => (double)r.ComputeTimeMs);
            var avgComm = ordered.Average(r => (double)r.CommunicationTimeMs);
            var avgTotal = ordered.Average(r => (double)r.TotalTimeMs);

            // Balans: rozkład sumy kandydatów na sesję klienta (client_id).
            var perSession = ordered
                .GroupBy(r => r.ClientId)
                .Select(s => (double)s.Sum(r => r.CandidatesCount))
                .ToList();

            var meanWork = perSession.Count > 0 ? perSession.Average() : 0;
            var imbalance = meanWork > 0 ? perSession.Max() / meanWork : 0;

            return new RunSummary
            {
                ExperimentName = g.Key,
                RunSequence = ordered.Min(r => r.RunSequence),
                ChunkSize = chunkSize,
                ClientThreads = ordered.First().ClientThreads,
                ConnectedClients = clients,
                Clients = plannedClients,
                GranularityPerClient = plannedClients > 0 ? (double)chunkSize / plannedClients : 0,
                CandidatesCount = candidates,
                DurationSeconds = seconds,
                TotalThroughput = candidates / seconds,
                AvgComputeMs = avgCompute,
                AvgCommMs = avgComm,
                CommFraction = avgTotal > 0 ? avgComm / avgTotal : 0,
                TaskCount = ordered.Count,
                SessionCount = perSession.Count,
                WorkImbalance = imbalance
            };
        })
        .Where(r => r.Clients > 0)
        .Where(r => r.GranularityPerClient > 0)
        .OrderBy(r => r.RunSequence)
        .ThenBy(r => r.ExperimentName)
        .ToList();
}

// Liczba klientów z nazwy eksperymentu, np. "..._clients_14_threads_0_...".
static int ParseClients(string experimentName, int fallback)
{
    var match = System.Text.RegularExpressions.Regex.Match(experimentName, @"clients_(\d+)");
    return match.Success ? int.Parse(match.Groups[1].Value) : fallback;
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
        .GroupBy(r => r.Clients)
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
        Path.Combine(outputDir, "total_throughput_by_clients.png"),
        integerXTicks: true);
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
        Path.Combine(outputDir, "total_throughput_by_threads.png"),
        integerXTicks: true);
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

static void SaveThroughputByGranularityAndClients(List<RunSummary> runs, string outputDir)
{
    var plot = new Plot();

    foreach (var group in runs.GroupBy(r => r.Clients).OrderBy(g => g.Key))
    {
        var points = group
            .GroupBy(r => r.GranularityPerClient)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                X = g.Key,
                Y = g.Average(r => r.TotalThroughput)
            })
            .ToList();

        if (points.Count == 0)
            continue;

        plot.Add.Scatter(
            points.Select(p => p.X).ToArray(),
            points.Select(p => p.Y).ToArray())
            .LegendText = $"{group.Key} clients";
    }

    plot.Title("Throughput vs granularity per client");
    plot.XLabel("Granularity per client = chunk size / clients");
    plot.YLabel("Total candidates / second");
    plot.ShowLegend();

    plot.SavePng(Path.Combine(outputDir, "throughput_by_granularity_and_clients.png"), 1600, 900);
}

static void SaveGranularityPlotsPerClientCount(List<RunSummary> runs, string outputDir)
{
    foreach (var group in runs.GroupBy(r => r.Clients).OrderBy(g => g.Key))
    {
        var points = group
            .GroupBy(r => r.GranularityPerClient)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                X = g.Key,
                Y = g.Average(r => r.TotalThroughput)
            })
            .ToList();

        SaveScatter(
            points.Select(p => p.X).ToArray(),
            points.Select(p => p.Y).ToArray(),
            $"Throughput vs granularity for {group.Key} clients",
            "Granularity per client = chunk size / clients",
            "Total candidates / second",
            Path.Combine(outputDir, $"throughput_by_granularity_for_{group.Key}_clients.png"));
    }
}

static void SaveClientPlotsPerGranularity(List<RunSummary> runs, string outputDir)
{
    foreach (var group in runs.GroupBy(r => r.GranularityPerClient).OrderBy(g => g.Key))
    {
        var points = group
            .GroupBy(r => r.Clients)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                X = (double)g.Key,
                Y = g.Average(r => r.TotalThroughput)
            })
            .ToList();

        var safeGranularity = group.Key.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', '_');

        SaveScatter(
            points.Select(p => p.X).ToArray(),
            points.Select(p => p.Y).ToArray(),
            $"Throughput vs clients for granularity {group.Key:0.###}",
            "Connected clients",
            "Total candidates / second",
            Path.Combine(outputDir, $"throughput_by_clients_for_granularity_{safeGranularity}.png"),
            integerXTicks: true);
    }
}

// Przyspieszenie S(n) = przepustowość(n klientów) / przepustowość(1 klient),
// liczone osobno dla każdej wielkości chunku. Linia "ideał" to S(n) = n.
static void SaveSpeedupByClients(List<RunSummary> runs, string outputDir)
{
    var plot = new Plot();

    foreach (var chunk in runs.GroupBy(r => r.ChunkSize).OrderBy(g => g.Key))
    {
        var baseline = chunk.Where(r => r.Clients == 1).Select(r => r.TotalThroughput).FirstOrDefault();
        if (baseline <= 0)
            continue;

        var pts = chunk.OrderBy(r => r.Clients).ToList();
        var line = plot.Add.Scatter(
            pts.Select(r => (double)r.Clients).ToArray(),
            pts.Select(r => r.TotalThroughput / baseline).ToArray());
        line.LegendText = $"chunk {Human(chunk.Key)}";
        line.LineWidth = 2;
        line.MarkerSize = 6;
    }

    AddIdealLinear(plot, runs.Max(r => r.Clients), "ideał S(n)=n");

    plot.Title("Przyspieszenie (speedup) względem 1 klienta");
    plot.XLabel("Liczba klientów");
    plot.YLabel("Speedup = przepustowość(n) / przepustowość(1)");
    plot.ShowLegend();
    IntegerXTicks(plot);
    plot.SavePng(Path.Combine(outputDir, "speedup_by_clients.png"), 1400, 800);
}

// Efektywność E(n) = S(n) / n. 1.0 = skalowanie idealne.
static void SaveEfficiencyByClients(List<RunSummary> runs, string outputDir)
{
    var plot = new Plot();

    foreach (var chunk in runs.GroupBy(r => r.ChunkSize).OrderBy(g => g.Key))
    {
        var baseline = chunk.Where(r => r.Clients == 1).Select(r => r.TotalThroughput).FirstOrDefault();
        if (baseline <= 0)
            continue;

        var pts = chunk.OrderBy(r => r.Clients).ToList();
        var line = plot.Add.Scatter(
            pts.Select(r => (double)r.Clients).ToArray(),
            pts.Select(r => r.TotalThroughput / baseline / r.Clients).ToArray());
        line.LegendText = $"chunk {Human(chunk.Key)}";
        line.LineWidth = 2;
        line.MarkerSize = 6;
    }

    var ideal = plot.Add.HorizontalLine(1.0);
    ideal.LinePattern = LinePattern.Dashed;
    ideal.Color = Colors.Gray;
    ideal.LegendText = "ideał E=1";

    plot.Title("Efektywność skalowania E(n) = S(n) / n");
    plot.XLabel("Liczba klientów");
    plot.YLabel("Efektywność");
    plot.ShowLegend();
    IntegerXTicks(plot);
    plot.SavePng(Path.Combine(outputDir, "efficiency_by_clients.png"), 1400, 800);
}

// Średni czas obliczeń i komunikacji na jedno zadanie, w funkcji wielkości chunku.
static void SaveComputeVsCommByChunk(List<RunSummary> runs, string outputDir)
{
    var byChunk = runs.GroupBy(r => r.ChunkSize).OrderBy(g => g.Key).ToList();
    var xs = Enumerable.Range(0, byChunk.Count).Select(i => (double)i).ToArray();

    var plot = new Plot();

    var compute = plot.Add.Scatter(xs, byChunk.Select(g => g.Average(r => r.AvgComputeMs)).ToArray());
    compute.LegendText = "obliczenia (compute)";
    compute.LineWidth = 2;
    compute.MarkerSize = 6;

    var comm = plot.Add.Scatter(xs, byChunk.Select(g => g.Average(r => r.AvgCommMs)).ToArray());
    comm.LegendText = "komunikacja (comm)";
    comm.LineWidth = 2;
    comm.MarkerSize = 6;

    SetChunkTicks(plot, byChunk.Select(g => g.Key).ToList());
    plot.Title("Czas na zadanie: obliczenia vs komunikacja");
    plot.XLabel("Wielkość chunku");
    plot.YLabel("Średni czas na zadanie [ms]");
    plot.ShowLegend();
    plot.SavePng(Path.Combine(outputDir, "compute_vs_comm_by_chunk.png"), 1400, 800);
}

// Udział komunikacji w czasie obsługi zadania (comm / total), wg wielkości chunku.
static void SaveCommOverheadByChunk(List<RunSummary> runs, string outputDir)
{
    var byChunk = runs.GroupBy(r => r.ChunkSize).OrderBy(g => g.Key).ToList();
    var xs = Enumerable.Range(0, byChunk.Count).Select(i => (double)i).ToArray();
    var ys = byChunk.Select(g => 100.0 * g.Average(r => r.CommFraction)).ToArray();

    var plot = new Plot();
    var line = plot.Add.Scatter(xs, ys);
    line.LineWidth = 2;
    line.MarkerSize = 6;

    SetChunkTicks(plot, byChunk.Select(g => g.Key).ToList());
    plot.Title("Narzut komunikacji w funkcji granulacji");
    plot.XLabel("Wielkość chunku");
    plot.YLabel("Udział komunikacji w czasie zadania [%]");
    plot.SavePng(Path.Combine(outputDir, "comm_overhead_by_chunk.png"), 1400, 800);
}

// Rozkład pracy (sumy kandydatów) na sesję klienta dla reprezentatywnych przebiegów.
// client_id jest generowany przy każdym połączeniu, więc liczba słupków rośnie wraz
// z reconnectami - to wprost pokazuje churn połączeń przy większej liczbie klientów.
static void SaveWorkPerSession(List<MetricRow> rows, string outputDir)
{
    foreach (var clients in new[] { 2, 5, 14 })
    {
        var run = rows
            .Where(r => r.ChunkSize == 1_000_000)
            .Where(r => ParseClients(r.ExperimentName, 0) == clients)
            .GroupBy(r => r.ExperimentName)
            .FirstOrDefault();

        if (run == null)
            continue;

        var perSession = run
            .GroupBy(r => r.ClientId)
            .Select(g => (double)g.Sum(r => r.CandidatesCount))
            .OrderByDescending(v => v)
            .ToArray();

        var plot = new Plot();
        plot.Add.Bars(perSession);

        plot.Axes.Margins(bottom: 0);
        plot.Title($"Praca na sesję klienta — {clients} klientów, chunk 1M ({perSession.Length} sesji)");
        plot.XLabel("Sesja klienta (posortowane malejąco)");
        plot.YLabel("Suma kandydatów");
        plot.SavePng(Path.Combine(outputDir, $"work_per_session_clients_{clients}_chunk_1000000.png"), 1400, 800);
    }
}

// Liczba odrębnych sesji (client_id) względem zamierzonej liczby klientów.
// Odstęp od linii ideału = skala reconnectów w trakcie przebiegu.
static void SaveSessionChurnByClients(List<RunSummary> runs, string outputDir)
{
    var pts = runs
        .GroupBy(r => r.Clients)
        .OrderBy(g => g.Key)
        .Select(g => new { X = (double)g.Key, Y = g.Average(r => (double)r.SessionCount) })
        .ToList();

    var plot = new Plot();
    var line = plot.Add.Scatter(pts.Select(p => p.X).ToArray(), pts.Select(p => p.Y).ToArray());
    line.LineWidth = 2;
    line.MarkerSize = 6;

    AddIdealLinear(plot, runs.Max(r => r.Clients), "ideał: sesje = klienci");

    plot.Title("Liczba sesji klientów vs liczba klientów (churn połączeń)");
    plot.XLabel("Liczba klientów");
    plot.YLabel("Średnia liczba odrębnych client_id");
    plot.ShowLegend();
    IntegerXTicks(plot);
    plot.SavePng(Path.Combine(outputDir, "session_churn_by_clients.png"), 1400, 800);
}

// Niezrównoważenie pracy = max(praca sesji) / średnia. 1.0 = idealnie równo.
static void SaveLoadImbalanceByClients(List<RunSummary> runs, string outputDir)
{
    var pts = runs
        .GroupBy(r => r.Clients)
        .OrderBy(g => g.Key)
        .Select(g => new { X = (double)g.Key, Y = g.Average(r => r.WorkImbalance) })
        .ToList();

    var plot = new Plot();
    var line = plot.Add.Scatter(pts.Select(p => p.X).ToArray(), pts.Select(p => p.Y).ToArray());
    line.LineWidth = 2;
    line.MarkerSize = 6;

    var ideal = plot.Add.HorizontalLine(1.0);
    ideal.LinePattern = LinePattern.Dashed;
    ideal.Color = Colors.Gray;
    ideal.LegendText = "ideał = 1";

    plot.Title("Niezrównoważenie pracy między sesjami");
    plot.XLabel("Liczba klientów");
    plot.YLabel("max / średnia kandydatów na sesję");
    plot.ShowLegend();
    IntegerXTicks(plot);
    plot.SavePng(Path.Combine(outputDir, "load_imbalance_by_clients.png"), 1400, 800);
}

// Przepustowość w funkcji DWÓCH parametrów naraz (klienci x chunk) jako mapa ciepła.
// Wiersze ułożone malejąco wg chunku, bo ScottPlot rysuje wiersz 0 u góry.
static void SaveThroughputHeatmap(List<RunSummary> runs, string outputDir)
{
    var clients = runs.Select(r => r.Clients).Distinct().OrderBy(v => v).ToList();
    var chunksDesc = runs.Select(r => r.ChunkSize).Distinct().OrderByDescending(v => v).ToList();

    var rows = chunksDesc.Count;
    var cols = clients.Count;
    var data = new double[rows, cols];

    for (var r = 0; r < rows; r++)
    for (var c = 0; c < cols; c++)
    {
        var cell = runs.FirstOrDefault(x => x.ChunkSize == chunksDesc[r] && x.Clients == clients[c]);
        data[r, c] = cell?.TotalThroughput ?? double.NaN;
    }

    var plot = new Plot();

    var heatmap = plot.Add.Heatmap(data);
    heatmap.Extent = new CoordinateRect(-0.5, cols - 0.5, -0.5, rows - 0.5);
    heatmap.Colormap = new ScottPlot.Colormaps.Viridis();

    var bar = plot.Add.ColorBar(heatmap);
    bar.Label = "Całkowita przepustowość [kandydatów/s]";

    plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
        clients.Select((n, i) => new Tick(i, n.ToString(CultureInfo.InvariantCulture))).ToArray());

    plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
        chunksDesc.Select((ch, r) => new Tick(rows - 1 - r, Human(ch))).ToArray());

    plot.Title("Przepustowość w funkcji liczby klientów i wielkości chunku");
    plot.XLabel("Liczba klientów");
    plot.YLabel("Wielkość chunku");
    plot.SavePng(Path.Combine(outputDir, "throughput_heatmap.png"), 1400, 800);
}

// Powierzchnia 3D z = przepustowość(klienci, chunk).
static void SaveThroughputSurface(List<RunSummary> runs, string outputDir)
{
    RenderSurface(
        runs, outputDir, "throughput_surface",
        "Przepustowość w funkcji liczby klientów i wielkości chunku",
        "Przepustowość [kandydatów/s]", "%.0s%c",
        (chunk, byChunk) => byChunk.TotalThroughput);
}

// Powierzchnia 3D z = efektywność E(n) = przyspieszenie / n (baza: 1 klient przy tym chunku).
// Pokazuje płaskowyż ~1,0 dla dużych chunków i urwisko dla małych.
static void SaveEfficiencySurface(List<RunSummary> runs, string outputDir)
{
    var baseline = runs
        .Where(r => r.Clients == 1)
        .ToDictionary(r => r.ChunkSize, r => r.TotalThroughput);

    RenderSurface(
        runs, outputDir, "efficiency_surface",
        "Efektywność skalowania w funkcji liczby klientów i wielkości chunku",
        "Efektywność E(n)", "%.1f",
        (chunk, byChunk) =>
        {
            var t1 = baseline.GetValueOrDefault(chunk);
            return t1 > 0 && byChunk.Clients > 0 ? byChunk.TotalThroughput / t1 / byChunk.Clients : 0;
        });
}

// ScottPlot nie ma 3D, więc dane i skrypt generuje C#, a renderuje gnuplot (uruchamiany stąd -
// bez osobnego kroku). value(chunk, run) wylicza wartość z dla danej komórki siatki.
static void RenderSurface(
    List<RunSummary> runs,
    string outputDir,
    string fileBase,
    string title,
    string zLabel,
    string zFormat,
    Func<long, RunSummary, double> value)
{
    var clients = runs.Select(r => r.Clients).Distinct().OrderBy(v => v).ToList();
    var chunks = runs.Select(r => r.ChunkSize).Distinct().OrderBy(v => v).ToList();

    // Dane jako siatka: bloki po stałym indeksie klienta, rozdzielone pustą linią.
    var data = new System.Text.StringBuilder();
    for (var i = 0; i < clients.Count; i++)
    {
        for (var j = 0; j < chunks.Count; j++)
        {
            var cell = runs.FirstOrDefault(r => r.Clients == clients[i] && r.ChunkSize == chunks[j]);
            var z = cell != null ? value(chunks[j], cell) : 0;
            data.AppendLine($"{i} {j} {z.ToString(CultureInfo.InvariantCulture)}");
        }
        data.AppendLine();
    }

    var dataPath = Path.Combine(outputDir, fileBase + ".dat");
    var scriptPath = Path.Combine(outputDir, fileBase + ".gp");
    var pngPath = Path.Combine(outputDir, fileBase + ".png");

    File.WriteAllText(dataPath, data.ToString());

    var xtics = string.Join(", ", clients.Select((n, i) => $"\"{n}\" {i}"));
    var ytics = string.Join(", ", chunks.Select((ch, j) => $"\"{Human(ch)}\" {j}"));

    var script = $"""
        set terminal pngcairo size 1400,800 enhanced font 'DejaVu Sans,12'
        set output '{pngPath}'
        set title "{title}"
        set xlabel "Liczba klientów" offset 0,-1
        set ylabel "Wielkość chunku" offset -1,0
        set zlabel "{zLabel}" rotate by 90
        set xtics ({xtics})
        set ytics ({ytics})
        set format z "{zFormat}"
        set grid
        set pm3d
        set hidden3d
        set view 55, 35
        set palette rgbformulae 33,13,10
        splot '{dataPath}' using 1:2:3 with pm3d notitle
        """;

    File.WriteAllText(scriptPath, script);

    if (!RunGnuplot(scriptPath))
        Console.WriteLine($"Warning: gnuplot not available, skipped {fileBase}.png (3D).");
}

static bool RunGnuplot(string scriptPath)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gnuplot",
            ArgumentList = { scriptPath },
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null)
            return false;

        process.WaitForExit();
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// Linia odniesienia y = x (idealne skalowanie liniowe).
static void AddIdealLinear(Plot plot, int maxX, string label)
{
    var line = plot.Add.Scatter(new double[] { 1, maxX }, new double[] { 1, maxX });
    line.LinePattern = LinePattern.Dashed;
    line.Color = Colors.Gray;
    line.MarkerSize = 0;
    line.LegendText = label;
}

// Etykiety osi X jako wielkości chunku (równe odstępy, czytelne wartości).
static void SetChunkTicks(Plot plot, List<long> chunks)
{
    var ticks = chunks
        .Select((c, i) => new Tick(i, Human(c)))
        .ToArray();
    plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
}

// 100000 -> "100k", 1000000 -> "1M".
static string Human(long value)
{
    if (value >= 1_000_000 && value % 1_000_000 == 0)
        return $"{value / 1_000_000}M";
    if (value >= 1000 && value % 1000 == 0)
        return $"{value / 1000}k";
    return value.ToString(CultureInfo.InvariantCulture);
}

static void SaveRunSummaryCsv(List<RunSummary> runs, string outputDir)
{
    var path = Path.Combine(outputDir, "run_summary.csv");

    var lines = new List<string>
    {
        "experiment_name,run_sequence,chunk_size,clients,connected_clients,granularity_per_client," +
        "client_threads,candidates_count,duration_seconds,total_throughput,task_count," +
        "avg_compute_ms,avg_comm_ms,comm_fraction,session_count,work_imbalance"
    };

    lines.AddRange(runs.Select(r => string.Join(",",
        Csv(r.ExperimentName),
        r.RunSequence.ToString(CultureInfo.InvariantCulture),
        r.ChunkSize.ToString(CultureInfo.InvariantCulture),
        r.Clients.ToString(CultureInfo.InvariantCulture),
        r.ConnectedClients.ToString(CultureInfo.InvariantCulture),
        r.GranularityPerClient.ToString(CultureInfo.InvariantCulture),
        r.ClientThreads.ToString(CultureInfo.InvariantCulture),
        r.CandidatesCount.ToString(CultureInfo.InvariantCulture),
        r.DurationSeconds.ToString(CultureInfo.InvariantCulture),
        r.TotalThroughput.ToString(CultureInfo.InvariantCulture),
        r.TaskCount.ToString(CultureInfo.InvariantCulture),
        r.AvgComputeMs.ToString(CultureInfo.InvariantCulture),
        r.AvgCommMs.ToString(CultureInfo.InvariantCulture),
        r.CommFraction.ToString(CultureInfo.InvariantCulture),
        r.SessionCount.ToString(CultureInfo.InvariantCulture),
        r.WorkImbalance.ToString(CultureInfo.InvariantCulture)
    )));

    File.WriteAllLines(path, lines);
}

static void SaveScatter(
    double[] xs,
    double[] ys,
    string title,
    string xLabel,
    string yLabel,
    string path,
    bool integerXTicks = false)
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
        IntegerTicksOnly = integerXTicks
    };

    plot.SavePng(path, 1400, 800);
}

// Oś X tylko liczby całkowite (np. liczba klientów - nie ma 1,5 klienta).
static void IntegerXTicks(Plot plot)
{
    plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
    {
        IntegerTicksOnly = true
    };
}

static string Csv(string value)
{
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return "\"" + value.Replace("\"", "\"\"") + "\"";

    return value;
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
    public long CommunicationTimeMs { get; set; }
    public long TotalTimeMs { get; set; }
    public double Throughput { get; set; }
    public int FoundCount { get; set; }
    public string ServerMachine { get; set; } = "";
    public int ConfiguredClients { get; set; }
    public DateTime TaskSentAtUtc { get; set; }
    public DateTime ResultReceivedAtUtc { get; set; }
}

class RunSummary
{
    public string ExperimentName { get; set; } = "";
    public int RunSequence { get; set; }
    public long ChunkSize { get; set; }
    public int ClientThreads { get; set; }
    public int ConnectedClients { get; set; }

    // Zamierzona liczba klientów z nazwy eksperymentu (clients_N) - stabilniejsza
    // niż configured_clients_count w CSV i niż liczba aktywnych połączeń.
    public int Clients { get; set; }

    public double GranularityPerClient { get; set; }
    public long CandidatesCount { get; set; }
    public double DurationSeconds { get; set; }
    public double TotalThroughput { get; set; }

    // Średnie czasy per zadanie i narzut komunikacji.
    public double AvgComputeMs { get; set; }
    public double AvgCommMs { get; set; }
    public double CommFraction { get; set; }
    public int TaskCount { get; set; }

    // Balans obciążenia liczony per sesja klienta (client_id).
    public int SessionCount { get; set; }
    public double WorkImbalance { get; set; }
}
