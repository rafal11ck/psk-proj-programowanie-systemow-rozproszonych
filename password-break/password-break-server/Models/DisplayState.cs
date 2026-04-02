namespace password_break_server.Models;

public record ClientDisplay(string Status, string Id, string Ip, string LastSeen, string Timeout);
public record TaskDisplay(string TaskId, string ClientId, string Range, string Elapsed);

public class DisplayState
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Found { get; set; }
    public int Remaining { get; set; }
    public bool AllFound => Remaining == 0 && Found > 0;
    public bool Saved { get; set; }

    public int TargetTotal { get; set; }
    public int InProgress { get; set; }
    public double TaskPercent { get; set; }
    public double FoundPercent { get; set; }

    public IReadOnlyList<ClientDisplay> Clients { get; set; } = [];
    public IReadOnlyList<TaskDisplay> ActiveTasks { get; set; } = [];
    public IReadOnlyList<string> AllLogLines { get; set; } = [];

    public bool ShowWorkers { get; set; } = true;
    public bool ShowTasks { get; set; } = true;
    public bool ShowLog { get; set; } = true;
    public string AttackMode { get; set; } = "";
}
