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
    public int HeartbeatTimeoutSeconds { get; set; } = 60;
    public List<string> TargetHashes { get; set; } = [];
}