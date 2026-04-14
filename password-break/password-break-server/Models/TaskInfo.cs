namespace password_break_server.Models;

public enum HashTaskStatus
{
    Pending,
    InProgress,
    Completed
}

public class TaskInfo
{
    public string TaskId { get; set; } = string.Empty;
    public HashTaskStatus Status { get; set; } = HashTaskStatus.Pending;
    public string? ClientId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime SentAtUtc { get; set; }
    public long StartIndex { get; set; }
    public long EndIndex { get; set; }
}