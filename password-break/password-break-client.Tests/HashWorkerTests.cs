using password_break_client;

namespace password_break_client.Tests;

public class HashWorkerTests
{
    [Fact]
    public void ComputeSha256_KnownPassword_ReturnsCorrectHash()
    {
        var hash = HashWorker.ComputeSha256("password");
        
        Assert.Equal("5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8", hash);
    }

    [Fact]
    public void ComputeSha256_EmptyString_ReturnsCorrectHash()
    {
        var hash = HashWorker.ComputeSha256("");
        
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public void ComputeSha256_SameInput_ReturnsSameHash()
    {
        var hash1 = HashWorker.ComputeSha256("test123");
        var hash2 = HashWorker.ComputeSha256("test123");
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeSha256_DifferentInputs_ReturnsDifferentHashes()
    {
        var hash1 = HashWorker.ComputeSha256("password1");
        var hash2 = HashWorker.ComputeSha256("password2");
        
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ProcessBruteForce_SingleChar_CorrectCount()
    {
        var results = HashWorker.ProcessBruteForce("abc", 1, 0, 2).ToList();
        
        Assert.Equal(3, results.Count);
        Assert.Equal(("a", HashWorker.ComputeSha256("a")), results[0]);
        Assert.Equal(("b", HashWorker.ComputeSha256("b")), results[1]);
        Assert.Equal(("c", HashWorker.ComputeSha256("c")), results[2]);
    }

    [Fact]
    public void ProcessBruteForce_SubsetRange_CorrectPasswords()
    {
        var results = HashWorker.ProcessBruteForce("abc", 2, 0, 2).ToList();
        
        Assert.Equal(3, results.Count);
        Assert.Equal("aa", results[0].Password);
        Assert.Equal("ab", results[1].Password);
        Assert.Equal("ac", results[2].Password);
    }

    [Fact]
    public void ProcessBruteForce_TwoCharCharset_CorrectCount()
    {
        var results = HashWorker.ProcessBruteForce("ab", 2, 0, 3).ToList();
        
        Assert.Equal(4, results.Count);
        Assert.Equal("aa", results[0].Password);
        Assert.Equal("ab", results[1].Password);
        Assert.Equal("ba", results[2].Password);
        Assert.Equal("bb", results[3].Password);
    }

    [Fact]
    public void ProcessWords_ReturnsCorrectHashes()
    {
        var results = HashWorker.ProcessWords(new[] { "hello", "world" }).ToList();
        
        Assert.Equal(2, results.Count);
        Assert.Equal(("hello", HashWorker.ComputeSha256("hello")), results[0]);
        Assert.Equal(("world", HashWorker.ComputeSha256("world")), results[1]);
    }

    [Fact]
    public void ProcessWords_EmptyList_ReturnsEmpty()
    {
        var results = HashWorker.ProcessWords(Array.Empty<string>()).ToList();
        
        Assert.Empty(results);
    }
}