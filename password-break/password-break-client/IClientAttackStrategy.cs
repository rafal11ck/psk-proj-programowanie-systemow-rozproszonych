namespace password_break_client;

public interface IClientAttackStrategy
{
    IEnumerable<(string Password, string Hash)> Process(long startIndex, long endIndex, HashSet<string> targetHashes, CancellationToken ct);
}

public class BruteForceClientStrategy : IClientAttackStrategy
{
    private readonly string _charSet;
    private readonly int _minLength;
    private readonly int _maxLength;

    public BruteForceClientStrategy(string charSet, int minLength, int maxLength)
    {
        _charSet = charSet;
        _minLength = minLength;
        _maxLength = maxLength;
    }

    public IEnumerable<(string Password, string Hash)> Process(long startIndex, long endIndex, HashSet<string> targetHashes, CancellationToken ct)
        => HashWorker.ProcessBruteForce(_charSet, _minLength, _maxLength, startIndex, endIndex, targetHashes);
}

public class DictionaryClientStrategy : IClientAttackStrategy
{
    private readonly IReadOnlyList<string> _wordList;

    public DictionaryClientStrategy(IReadOnlyList<string> wordList)
    {
        _wordList = wordList;
    }

    public IEnumerable<(string Password, string Hash)> Process(long startIndex, long endIndex, HashSet<string> targetHashes, CancellationToken ct)
        => HashWorker.ProcessDictionary(_wordList, startIndex, endIndex, targetHashes);
}
