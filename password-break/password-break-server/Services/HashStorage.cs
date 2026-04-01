namespace password_break_server.Services;

public class HashStorage
{
    private readonly Dictionary<string, string> _hashes = new();
    private readonly Lock _lock = new();

    public void StoreBatch(IEnumerable<(string Password, string Hash)> entries)
    {
        lock (_lock)
        {
            foreach (var (password, hash) in entries)
            {
                _hashes.TryAdd(password, hash);
            }
        }
    }

    public string? FindPassword(string hash)
    {
        lock (_lock)
        {
            return _hashes.FirstOrDefault(kvp => kvp.Value == hash).Key;
        }
    }

    public string? FindHash(string password)
    {
        lock (_lock)
        {
            return _hashes.TryGetValue(password, out var hash) ? hash : null;
        }
    }

    public int Count
    {
        get { lock (_lock) { return _hashes.Count; } }
    }

    public void SaveToFile(string filePath)
    {
        lock (_lock)
        {
            var lines = _hashes.Select(kvp => $"{kvp.Key},{kvp.Value}");
            File.WriteAllLines(filePath, lines.Prepend("password,hash"));
        }
    }
}