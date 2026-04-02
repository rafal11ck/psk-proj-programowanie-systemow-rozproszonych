using password_break_server.Models;

namespace password_break_server.Services;

public interface ITaskManager
{
    IReadOnlyList<string> TargetHashes { get; }
    long GetWordListTimestamp();
    TaskInfo? GetNextTask(string clientId);
    void MarkCompleted(string taskId);
    void MarkPending(string taskId);
    List<string> RequeueClientTasks(string clientId);
    (int Completed, int Total, int Pending) GetProgress();
    List<TaskInfo> GetActiveTasks();
}
