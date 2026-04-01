namespace password_break_server.Models;

public class PasswordBreakConfig
{
    public string AttackMode { get; set; } = "bruteforce";
    public string CharSet { get; set; } = "abcdefghijklmnopqrstuvwxyz";
    public int MinLength { get; set; } = 1;
    public int MaxLength { get; set; } = 6;
    public int ChunkSize { get; set; } = 10000;
    public string? WordListPath { get; set; }
    public int HeartbeatTimeoutSeconds { get; set; } = 60;
}

public class TaskInfo
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? ClientId { get; set; }
    public DateTime AssignedAt { get; set; }
    public object TaskData { get; set; } = null!;
}

public class BruteForceTaskData
{
    public string CharSet { get; set; } = string.Empty;
    public int Length { get; set; }
    public long StartIndex { get; set; }
    public long EndIndex { get; set; }
}