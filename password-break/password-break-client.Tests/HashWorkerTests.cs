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
    public void ProcessBruteForce_OnlyReturnsMatchingHashes()
    {
        var targetHash = HashWorker.ComputeSha256("b");
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetHash };
        
        var results = HashWorker.ProcessBruteForce("abc", 1, 1, 0, 2, targetHashes).ToList();
        
        Assert.Single(results);
        Assert.Equal("b", results[0].Password);
        Assert.Equal(targetHash, results[0].Hash);
    }

    [Fact]
    public void ProcessBruteForce_NoMatches_ReturnsEmpty()
    {
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "nonexistent" };
        
        var results = HashWorker.ProcessBruteForce("abc", 1, 1, 0, 2, targetHashes).ToList();
        
        Assert.Empty(results);
    }

    [Fact]
    public void ProcessBruteForce_MultipleMatches_ReturnsAll()
    {
        var hashA = HashWorker.ComputeSha256("a");
        var hashB = HashWorker.ComputeSha256("b");
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hashA, hashB };
        
        var results = HashWorker.ProcessBruteForce("abc", 1, 1, 0, 2, targetHashes).ToList();
        
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ProcessBruteForce_MultipleLengths_IteratesCorrectly()
    {
        var hashA1 = HashWorker.ComputeSha256("a");
        var hashAA = HashWorker.ComputeSha256("aa");
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hashA1, hashAA };
        
        var results = HashWorker.ProcessBruteForce("a", 1, 2, 0, 1, targetHashes).ToList();
        
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void ProcessBruteForce_CrossLengthRange()
    {
        var hashA = HashWorker.ComputeSha256("a");
        var hashB = HashWorker.ComputeSha256("b");
        var hashAA = HashWorker.ComputeSha256("aa");
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hashA, hashB, hashAA };
        
        var results = HashWorker.ProcessBruteForce("ab", 1, 2, 0, 5, targetHashes).ToList();
        
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void ProcessDictionary_OnlyReturnsMatchingHashes()
    {
        var targetHash = HashWorker.ComputeSha256("world");
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetHash };
        var wordList = new List<string> { "hello", "world", "test" };
        
        var results = HashWorker.ProcessDictionary(wordList, 0, 2, targetHashes).ToList();
        
        Assert.Single(results);
        Assert.Equal("world", results[0].Password);
    }

    [Fact]
    public void ProcessDictionary_RangeLimits_RespectsRange()
    {
        var hashA = HashWorker.ComputeSha256("a");
        var hashB = HashWorker.ComputeSha256("b");
        var hashC = HashWorker.ComputeSha256("c");
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hashA, hashB, hashC };
        var wordList = new List<string> { "a", "b", "c", "d" };
        
        var results = HashWorker.ProcessDictionary(wordList, 0, 1, targetHashes).ToList();

        var passwords = results.Select(r => r.Password).ToHashSet();

        Assert.Equal(2, passwords.Count);
        Assert.Contains("a", passwords);
        Assert.Contains("b", passwords);
    }

    [Fact]
    public void ProcessDictionary_EmptyList_ReturnsEmpty()
    {
        var targetHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hash" };
        var wordList = new List<string>();
        
        var results = HashWorker.ProcessDictionary(wordList, 0, 10, targetHashes).ToList();
        
        Assert.Empty(results);
    }
}