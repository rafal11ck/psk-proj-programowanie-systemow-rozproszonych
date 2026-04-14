using Microsoft.Extensions.Logging;

namespace password_break_client;

public interface IWordlistManager
{
    long GetLocalTimestamp();
    Task DownloadAsync(string serverUrl);
    List<string> Load();
}

public class WordlistManager : IWordlistManager
{
    private static readonly string LocalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wordlist.txt");
    private static readonly HttpClient SharedHttpClient = new();
    private readonly ILogger<WordlistManager> _logger;

    public WordlistManager(ILogger<WordlistManager> logger)
    {
        _logger = logger;
    }

    public long GetLocalTimestamp()
    {
        var info = new FileInfo(LocalPath);
        return info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
    }

    public async Task DownloadAsync(string serverUrl)
    {
        var uri = new Uri(serverUrl);
        var wordlistUrl = $"{uri.Scheme}://{uri.Host}:8081/wordlist";

        var content = await SharedHttpClient.GetStringAsync(wordlistUrl);

        await File.WriteAllTextAsync(LocalPath, content);
        _logger.LogInformation("Downloaded wordlist to {Path}", LocalPath);
    }

    public List<string> Load()
    {
        if (!File.Exists(LocalPath))
        {
            _logger.LogWarning("Wordlist not found: {Path}", LocalPath);
            return [];
        }

        var words = File.ReadAllLines(LocalPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();
        _logger.LogInformation("Loaded wordlist ({Count} words)", words.Count);
        return words;
    }
}
