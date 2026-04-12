namespace password_break_client;

public interface IClientAttackStrategy
{
    List<(string Password, string Hash)> Process(
        long startIndex,
        long endIndex,
        HashSet<string> targetHashes,
        CancellationToken ct);
}

public class BruteForceClientStrategy : IClientAttackStrategy
{
    private readonly string _charSet;
    private readonly int _minLength;
    private readonly int _maxLength;
    private readonly int _degreeOfParallelism;

    public BruteForceClientStrategy(string charSet, int minLength, int maxLength, int? degreeOfParallelism = null)
    {
        _charSet = charSet;
        _minLength = minLength;
        _maxLength = maxLength;
        _degreeOfParallelism = Math.Max(1, degreeOfParallelism ?? Environment.ProcessorCount);
    }

    public List<(string Password, string Hash)> Process(long startIndex, long endIndex, HashSet<string> targetHashes, CancellationToken ct)
        => HashWorker.ProcessBruteForceParallel(
            _charSet,
            _minLength,
            _maxLength,
            startIndex,
            endIndex,
            targetHashes,
            _degreeOfParallelism,
            ct);
}

public class DictionaryClientStrategy : IClientAttackStrategy
{
    private readonly IReadOnlyList<string> _wordList;
    private readonly int _degreeOfParallelism;

    public DictionaryClientStrategy(IReadOnlyList<string> wordList, int? degreeOfParallelism = null)
    {
        _wordList = wordList;
        _degreeOfParallelism = Math.Max(1, degreeOfParallelism ?? Environment.ProcessorCount);
    }

    public List<(string Password, string Hash)> Process(long startIndex, long endIndex, HashSet<string> targetHashes, CancellationToken ct)
        => HashWorker.ProcessDictionaryParallel(
            _wordList,
            startIndex,
            endIndex,
            targetHashes,
            _degreeOfParallelism,
            ct);
}
