using password_break_server.Services;

namespace password_break_server.Tests;

public class HashStorageTests
{
    [Fact]
    public void StoreBatch_AddsHashes()
    {
        var storage = new HashStorage();
        var entries = new[]
        {
            ("password1", "hash1"),
            ("password2", "hash2")
        };

        storage.StoreBatch(entries);

        Assert.Equal(2, storage.Count);
    }

    [Fact]
    public void StoreBatch_DuplicatePasswords_KeepsOnlyOne()
    {
        var storage = new HashStorage();
        var entries = new[]
        {
            ("password", "hash1"),
            ("password", "hash2")
        };

        storage.StoreBatch(entries);

        Assert.Equal(1, storage.Count);
    }

    [Fact]
    public void FindPassword_ReturnsCorrectPassword()
    {
        var storage = new HashStorage();
        storage.StoreBatch(new[] { ("secret", "abc123") });

        var result = storage.FindPassword("abc123");

        Assert.Equal("secret", result);
    }

    [Fact]
    public void FindPassword_NotFound_ReturnsNull()
    {
        var storage = new HashStorage();

        var result = storage.FindPassword("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void FindHash_ReturnsCorrectHash()
    {
        var storage = new HashStorage();
        storage.StoreBatch(new[] { ("secret", "abc123") });

        var result = storage.FindHash("secret");

        Assert.Equal("abc123", result);
    }

    [Fact]
    public void FindHash_NotFound_ReturnsNull()
    {
        var storage = new HashStorage();

        var result = storage.FindHash("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        var storage = new HashStorage();
        Assert.Equal(0, storage.Count);

        storage.StoreBatch(new[] { ("p1", "h1"), ("p2", "h2") });
        Assert.Equal(2, storage.Count);
    }

    [Fact]
    public void SaveToFile_WritesCorrectFormat()
    {
        var storage = new HashStorage();
        storage.StoreBatch(new[] { ("pass1", "hash1"), ("pass2", "hash2") });
        var tempFile = Path.GetTempFileName();

        try
        {
            storage.SaveToFile(tempFile);
            var lines = File.ReadAllLines(tempFile);

            Assert.Equal(3, lines.Length);
            Assert.Equal("password,hash", lines[0]);
            Assert.Contains("pass1,hash1", lines);
            Assert.Contains("pass2,hash2", lines);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}