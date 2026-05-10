using System.Text.RegularExpressions;
using password_break_server.Models;

namespace password_break_server.Services;

public sealed class ExperimentRun
{
    public required int Sequence { get; init; }
    public required string RunId { get; init; }
    public required string ExperimentName { get; init; }
    public required string DirectoryPath { get; init; }
    public required string MetricsFilePath { get; init; }
    public required string ResultsFilePath { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? PausedAtUtc { get; set; }
    public required int ConnectedClientsAtStart { get; init; }

    public bool IsOpen => PausedAtUtc == null;
}

public class ExperimentRunManager
{
    private readonly PasswordBreakConfig _config;
    private readonly object _lock = new();
    private int _runNo;

    public ExperimentRun? Current { get; private set; }

    public ExperimentRunManager(PasswordBreakConfig config)
    {
        _config = config;
    }

    public ExperimentRun StartNewRun(int connectedClients)
    {
        lock (_lock)
        {
            _runNo++;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            var baseName = Regex.Replace(_config.ExperimentName, @"_?clients_\d+", "");
            baseName = Regex.Replace(baseName, @"_?threads_\d+", "");
            baseName = Regex.Replace(baseName, @"_?chunk_\d+", "");
            baseName = MakeSafeFileName(baseName);

            var experimentName =
                $"{baseName}_clients_{connectedClients}_threads_{_config.ClientThreads}_chunk_{_config.ChunkSize}_run_{_runNo}_{timestamp}";

            var dir = Path.Combine("experiments", experimentName);
            Directory.CreateDirectory(dir);

            var run = new ExperimentRun
            {
                Sequence = _runNo,
                RunId = $"{_config.RunId}_{_runNo}",
                ExperimentName = experimentName,
                DirectoryPath = dir,
                MetricsFilePath = Path.Combine(dir, "metrics.csv"),
                ResultsFilePath = Path.Combine(dir, "results.csv"),
                StartedAtUtc = DateTime.UtcNow,
                ConnectedClientsAtStart = connectedClients
            };

            EnsureMetricsFileExists(run.MetricsFilePath);
            Current = run;

            Console.WriteLine($"Experiment directory: {run.DirectoryPath}");
            Console.WriteLine($"Metrics file: {run.MetricsFilePath}");
            Console.WriteLine($"Results file: {run.ResultsFilePath}");

            return run;
        }
    }

    public void PauseCurrentRun()
    {
        lock (_lock)
        {
            if (Current != null && Current.PausedAtUtc == null)
                Current.PausedAtUtc = DateTime.UtcNow;
        }
    }

    private static void EnsureMetricsFileExists(string path)
    {
        File.WriteAllText(
            path,
            "run_id,experiment_name,run_sequence,run_started_at_utc,run_paused_at_utc," +
            "server_machine,attack_mode,chunk_size,min_length,max_length,charset_length,target_hashes_count," +
            "configured_clients_count,connected_clients_at_start,connected_clients_at_result,client_threads," +
            "task_id,client_id,start_index,end_index,candidates_count," +
            "compute_time_ms,communication_time_ms,total_time_ms,throughput_candidates_per_sec,found_count," +
            "task_sent_at_utc,result_received_at_utc" +
            Environment.NewLine);
    }

    private static string MakeSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "experiment";

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return value.Trim('_', ' ');
    }
}
