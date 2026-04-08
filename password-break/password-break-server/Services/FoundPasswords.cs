namespace password_break_server.Services;

public class FoundPasswords : IFoundPasswords
{
    private readonly Dictionary<string, string> _found = new();
    private readonly HashSet<string> _remainingHashes;
    private readonly Lock _lock = new();
    private bool _saved;

    public event Action? OnAllFound;

    public FoundPasswords(IEnumerable<string> targetHashes)
    {
        _remainingHashes = new HashSet<string>(targetHashes, StringComparer.OrdinalIgnoreCase);
    }

    public void StoreFound(IEnumerable<(string Password, string Hash)> entries)
    {
        bool nowAllFound;
        lock (_lock)
        {
            var wasAll = _remainingHashes.Count == 0;
            foreach (var (password, hash) in entries)
            {
                if (_remainingHashes.Contains(hash))
                {
                    _found.TryAdd(hash, password);
                    _remainingHashes.Remove(hash);
                }
            }
            nowAllFound = !wasAll && _remainingHashes.Count == 0;
        }
        if (nowAllFound)
            OnAllFound?.Invoke();
    }

    public bool AllFound
    {
        get { lock (_lock) { return _remainingHashes.Count == 0; } }
    }

    public bool Saved
    {
        get { lock (_lock) { return _saved; } }
    }

    public IReadOnlyDictionary<string, string> GetAllFound()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_found);
        }
    }

    public int FoundCount
    {
        get { lock (_lock) { return _found.Count; } }
    }

    public int RemainingCount
    {
        get { lock (_lock) { return _remainingHashes.Count; } }
    }

    public void SaveToFile(string filePath)
    {
        lock (_lock)
        {
            if (_saved) return;
            _saved = true;
            var lines = _found.Select(kvp => $"{kvp.Value},{kvp.Key}");
            File.WriteAllLines(filePath, lines.Prepend("password,hash"));
        }
    }
}
