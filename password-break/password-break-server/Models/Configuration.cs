namespace password_break_server.Models;

public class PasswordBreakConfig
{
    public string AttackMode { get; set; } = "bruteforce";
    public string CharSet { get; set; } = "abcdefghijklmnopqrstuvwxyz";
    public int MinLength { get; set; } = 1;
    public int MaxLength { get; set; } = 10;
    public int ChunkSize { get; set; } = 10000;
    public string? WordListPath { get; set; }
    public int HeartbeatIntervalSeconds { get; set; } = 15;
    /// <summary>
    /// Po ilu sekundach bez heartbeat'a od klienta serwer ubije jego stream
    /// i potraktuje jako rozłączonego (taski wracają do kolejki).
    /// 0 wyłącza mechanizm — wtedy zostaje tylko Kestrel HTTP/2 keepalive.
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 60;
    /// <summary>
    /// Maksymalny czas (sekundy), przez który task może być w stanie InProgress
    /// zanim zostanie automatycznie zwrócony do kolejki. Chroni przed zacięciem
    /// klienta na pojedynczym chunk'u. 0 wyłącza mechanizm.
    /// </summary>
    public int TaskTimeoutSeconds { get; set; } = 300;
    public List<string> TargetHashes { get; set; } = [];
    public string RunId { get; set; } = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    public string ExperimentName { get; set; } = "default";
    public int ClientsCount { get; set; } = 1;
    public int ClientThreads { get; set; } = 0;
}