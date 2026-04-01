using password_break_server.Services;
using password_break_server.Models;

namespace password_break_server.Tests;

public class TaskManagerTests
{
    [Fact]
    public void GetNextTask_ReturnsFirstTask()
    {
        var config = new PasswordBreakConfig
        {
            AttackMode = "bruteforce",
            CharSet = "abc",
            MinLength = 1,
            MaxLength = 1,
            ChunkSize = 10
        };
        var manager = new TaskManager(config);

        var task = manager.GetNextTask("client1");

        Assert.NotNull(task);
        Assert.Equal("bf_1_0", task.TaskId);
    }

    [Fact]
    public void GetNextTask_NoMoreTasks_ReturnsNull()
    {
        var config = new PasswordBreakConfig
        {
            AttackMode = "bruteforce",
            CharSet = "a",
            MinLength = 1,
            MaxLength = 1,
            ChunkSize = 10
        };
        var manager = new TaskManager(config);

        manager.GetNextTask("client1");
        manager.MarkCompleted("bf_1_0");
        var task = manager.GetNextTask("client2");

        Assert.Null(task);
    }

    [Fact]
    public void GetNextTask_AssignsToClient()
    {
        var config = new PasswordBreakConfig
        {
            AttackMode = "bruteforce",
            CharSet = "abc",
            MinLength = 1,
            MaxLength = 1,
            ChunkSize = 10
        };
        var manager = new TaskManager(config);

        var task = manager.GetNextTask("client123");

        Assert.Equal("client123", task!.ClientId);
        Assert.Equal("in_progress", task.Status);
    }

    [Fact]
    public void MarkCompleted_UpdatesStatus()
    {
        var config = new PasswordBreakConfig
        {
            AttackMode = "bruteforce",
            CharSet = "abc",
            MinLength = 1,
            MaxLength = 1,
            ChunkSize = 10
        };
        var manager = new TaskManager(config);

        var task = manager.GetNextTask("client1");
        manager.MarkCompleted(task!.TaskId);

        var nextTask = manager.GetNextTask("client2");
        Assert.Null(nextTask);
    }

    [Fact]
    public void MarkPending_ReturnsTaskToQueue()
    {
        var config = new PasswordBreakConfig
        {
            AttackMode = "bruteforce",
            CharSet = "a",
            MinLength = 1,
            MaxLength = 1,
            ChunkSize = 10
        };
        var manager = new TaskManager(config);

        var task1 = manager.GetNextTask("client1");
        manager.MarkPending(task1!.TaskId);
        
        var task2 = manager.GetNextTask("client2");

        Assert.NotNull(task2);
        Assert.Equal(task1.TaskId, task2!.TaskId);
    }

    [Fact]
    public void TaskManager_DictionaryMode_GeneratesWordTasks()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllLines(tempFile, new[] { "password1", "password2", "password3" });

        try
        {
            var config = new PasswordBreakConfig
            {
                AttackMode = "dictionary",
                WordListPath = tempFile,
                ChunkSize = 2
            };
            var manager = new TaskManager(config);

            var task1 = manager.GetNextTask("client1");
            var task2 = manager.GetNextTask("client2");

            Assert.NotNull(task1);
            Assert.NotNull(task2);
            Assert.StartsWith("dict_", task1!.TaskId);
            Assert.StartsWith("dict_", task2!.TaskId);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void TaskManager_BruteForceGeneratesCorrectRange()
    {
        var config = new PasswordBreakConfig
        {
            AttackMode = "bruteforce",
            CharSet = "abc",
            MinLength = 2,
            MaxLength = 2,
            ChunkSize = 3
        };
        var manager = new TaskManager(config);

        var task = manager.GetNextTask("client1");
        var data = task!.TaskData as BruteForceTaskData;

        Assert.NotNull(data);
        Assert.Equal("abc", data!.CharSet);
        Assert.Equal(2, data.Length);
        Assert.Equal(0, data.StartIndex);
        Assert.Equal(2, data.EndIndex);
    }
}