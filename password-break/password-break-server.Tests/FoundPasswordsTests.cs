using password_break_server.Services;

namespace password_break_server.Tests;

public class FoundPasswordsTests
{
    [Fact]
    public void StoreFound_OnlyStoresTargetHashes()
    {
        var found = new FoundPasswords(new[] { "hash1", "hash2", "hash3" });
        var entries = new[]
        {
            ("password1", "hash1"),
            ("password2", "hash2"),
            ("password3", "hash3")
        };

        found.StoreFound(entries);

        Assert.Equal(3, found.FoundCount);
        Assert.Equal(0, found.RemainingCount);
    }

    [Fact]
    public void StoreFound_DuplicateHashes_KeepsFirst()
    {
        var found = new FoundPasswords(new[] { "hash1" });
        var entries = new[]
        {
            ("password1", "hash1"),
            ("password2", "hash1")
        };

        found.StoreFound(entries);

        Assert.Equal(1, found.FoundCount);
        var allFound = found.GetAllFound();
        Assert.Equal("password1", allFound["hash1"]);
    }

    [Fact]
    public void StoreFound_CaseInsensitiveHashes()
    {
        var found = new FoundPasswords(new[] { "HASH1" });
        var entries = new[] { ("password1", "hash1") };

        found.StoreFound(entries);

        Assert.Equal(1, found.FoundCount);
        Assert.Equal(0, found.RemainingCount);
    }

    [Fact]
    public void AllFound_ReturnsFalseWhenRemaining()
    {
        var found = new FoundPasswords(new[] { "hash1", "hash2" });
        found.StoreFound(new[] { ("password1", "hash1") });

        Assert.False(found.AllFound);
    }

    [Fact]
    public void AllFound_ReturnsTrueWhenNoneRemaining()
    {
        var found = new FoundPasswords(new[] { "hash1", "hash2" });
        found.StoreFound(new[] { ("password1", "hash1"), ("password2", "hash2") });

        Assert.True(found.AllFound);
    }

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        var found = new FoundPasswords(new[] { "hash1", "hash2", "hash3" });
        Assert.Equal(0, found.FoundCount);
        Assert.Equal(3, found.RemainingCount);

        found.StoreFound(new[] { ("p1", "h1"), ("p2", "h2") });
        Assert.Equal(0, found.FoundCount);
        Assert.Equal(3, found.RemainingCount);

        found.StoreFound(new[] { ("p1", "hash1"), ("p2", "hash2") });
        Assert.Equal(2, found.FoundCount);
        Assert.Equal(1, found.RemainingCount);
    }

    [Fact]
    public void SaveToFile_WritesCorrectFormat()
    {
        var found = new FoundPasswords(new[] { "hash1", "hash2" });
        found.StoreFound(new[] { ("pass1", "hash1"), ("pass2", "hash2") });
        var tempFile = Path.GetTempFileName();

        try
        {
            found.SaveToFile(tempFile);
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