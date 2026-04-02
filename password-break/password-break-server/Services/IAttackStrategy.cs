namespace password_break_server.Services;

public interface IAttackStrategy
{
    long GetTotalItems();
    Config BuildConfig(Models.PasswordBreakConfig config, long wordlistTimestamp);
}

public static class AttackStrategyHelper
{
    public static Config CreateBaseConfig(Models.PasswordBreakConfig config, long wordlistTimestamp)
    {
        var c = new Config
        {
            ChunkSize = config.ChunkSize,
            WordlistTimestamp = wordlistTimestamp,
            HeartbeatIntervalSeconds = config.HeartbeatIntervalSeconds
        };
        c.TargetHashes.AddRange(config.TargetHashes);
        return c;
    }
}

public class BruteForceStrategy : IAttackStrategy
{
    private readonly Models.PasswordBreakConfig _config;

    public BruteForceStrategy(Models.PasswordBreakConfig config)
    {
        _config = config;
    }

    public long GetTotalItems()
    {
        long total = 0;
        for (var length = _config.MinLength; length <= _config.MaxLength; length++)
            total += (long)Math.Pow(_config.CharSet.Length, length);
        return total;
    }

    public Config BuildConfig(Models.PasswordBreakConfig config, long wordlistTimestamp)
    {
        var grpcConfig = AttackStrategyHelper.CreateBaseConfig(config, wordlistTimestamp);
        grpcConfig.BruteForce = new BruteForceConfig
        {
            Charset = config.CharSet,
            MinLength = config.MinLength,
            MaxLength = config.MaxLength
        };
        return grpcConfig;
    }
}

public class DictionaryStrategy : IAttackStrategy
{
    private readonly Models.PasswordBreakConfig _config;
    private readonly List<string> _wordList = [];

    public DictionaryStrategy(Models.PasswordBreakConfig config)
    {
        _config = config;
        LoadWordList();
    }

    public long GetTotalItems() => _wordList.Count;

    public Config BuildConfig(Models.PasswordBreakConfig config, long wordlistTimestamp)
    {
        var grpcConfig = AttackStrategyHelper.CreateBaseConfig(config, wordlistTimestamp);
        grpcConfig.Dictionary = new DictionaryConfig { WordlistPath = config.WordListPath ?? "" };
        return grpcConfig;
    }

    private void LoadWordList()
    {
        if (string.IsNullOrEmpty(_config.WordListPath))
            return;

        if (!File.Exists(_config.WordListPath))
        {
            Console.Error.WriteLine($"[WARN] Wordlist file not found: {_config.WordListPath}");
            return;
        }

        var lines = File.ReadAllLines(_config.WordListPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim());
        _wordList.AddRange(lines);
    }
}
