using password_break_server.Services;
using password_break_server.Models;

namespace password_break_server.Tests;

public class TaskManagerTests
{
    private FoundPasswords CreateFoundPasswords() => new(["test_hash_1", "test_hash_2"]);

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
        var manager = new TaskManager(config, CreateFoundPasswords());

        var task = manager.GetNextTask("client1");

        Assert.NotNull(task);
        Assert.Equal("0_2", task.TaskId);
        Assert.Equal(0, task.StartIndex);
        Assert.Equal(2, task.EndIndex);
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
        var manager = new TaskManager(config, CreateFoundPasswords());

        manager.GetNextTask("client1");
        manager.MarkCompleted("0_0");
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
        var manager = new TaskManager(config, CreateFoundPasswords());

        var task = manager.GetNextTask("client123");

        Assert.Equal("client123", task!.ClientId);
        Assert.Equal(HashTaskStatus.InProgress, task.Status);
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
        var manager = new TaskManager(config, CreateFoundPasswords());

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
        var manager = new TaskManager(config, CreateFoundPasswords());

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
            var manager = new TaskManager(config, CreateFoundPasswords());

            var task1 = manager.GetNextTask("client1");
            var task2 = manager.GetNextTask("client2");

            Assert.NotNull(task1);
            Assert.NotNull(task2);
            Assert.Equal("0_1", task1!.TaskId);
            Assert.Equal("2_2", task2!.TaskId);
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
            MinLength = 1,
            MaxLength = 1,
            ChunkSize = 10
        };
        var manager = new TaskManager(config, CreateFoundPasswords());

        var task = manager.GetNextTask("client1");

        Assert.NotNull(task);
        Assert.Equal(0, task!.StartIndex);
        Assert.Equal(2, task.EndIndex);
    }

    [Fact]
    public void TaskManager_DictionaryGeneratesCorrectRange()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllLines(tempFile, new[] { "a", "b", "c", "d", "e" });

        try
        {
            var config = new PasswordBreakConfig
            {
                AttackMode = "dictionary",
                WordListPath = tempFile,
                ChunkSize = 2
            };
            var manager = new TaskManager(config, CreateFoundPasswords());

            var task = manager.GetNextTask("client1");

            Assert.NotNull(task);
            Assert.Equal(0, task!.StartIndex);
            Assert.Equal(1, task.EndIndex);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void TaskManager_DictionaryGeneratesCorrectRange()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllLines(tempFile, new[] { "a", "b", "c", "d", "e" });

        try
        {
            var config = new PasswordBreakConfig
            {
                AttackMode = "dictionary",
                WordListPath = tempFile,
                ChunkSize = 2
            };
            var manager = CreateManager(config);

            var task = manager.GetNextTask("client1");

            Assert.NotNull(task);
            Assert.Equal(0, task!.StartIndex);
            Assert.Equal(1, task.EndIndex);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
