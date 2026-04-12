using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace password_break_client;

public static class HashWorker
{
    private const long CancellationCheckMask = 0xFFF;

    public static string ComputeSha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static IEnumerable<(string Password, string Hash)> ProcessBruteForce(
        string charset,
        int minLength,
        int maxLength,
        long startIndex,
        long endIndex,
        HashSet<string> targetHashes)
    {
        return ProcessBruteForceParallel(
            charset,
            minLength,
            maxLength,
            startIndex,
            endIndex,
            targetHashes,
            1,
            CancellationToken.None);
    }

    public static IEnumerable<(string Password, string Hash)> ProcessDictionary(
        IReadOnlyList<string> wordList,
        long startIndex,
        long endIndex,
        HashSet<string> targetHashes)
    {
        return ProcessDictionaryParallel(
            wordList,
            startIndex,
            endIndex,
            targetHashes,
            1,
            CancellationToken.None);
    }

    public static List<(string Password, string Hash)> ProcessBruteForceParallel(
        string charset,
        int minLength,
        int maxLength,
        long startIndex,
        long endIndex,
        HashSet<string> targetHashes,
        int degreeOfParallelism,
        CancellationToken ct)
    {
        if (endIndex < startIndex)
            return [];

        var ranges = SplitRange(startIndex, endIndex, degreeOfParallelism);
        var results = new ConcurrentBag<(string Password, string Hash)>();

        Parallel.ForEach(
            ranges,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = degreeOfParallelism,
                CancellationToken = ct
            },
            range =>
            {
                long absoluteIndex = range.Start;
                var (length, localIndex) = GetLengthAndLocalIndex(charset, minLength, maxLength, range.Start);

                while (absoluteIndex <= range.End && length <= maxLength)
                {
                    if ((absoluteIndex & CancellationCheckMask) == 0)
                        ct.ThrowIfCancellationRequested();

                    var password = IndexToPassword(localIndex, charset, length);
                    var hash = ComputeSha256(password);

                    if (targetHashes.Contains(hash))
                        results.Add((password, hash));

                    absoluteIndex++;
                    localIndex++;

                    var maxForLength = (long)Math.Pow(charset.Length, length);
                    if (localIndex >= maxForLength)
                    {
                        localIndex = 0;
                        length++;
                    }
                }
            });

        return results.ToList();
    }

    public static List<(string Password, string Hash)> ProcessDictionaryParallel(
        IReadOnlyList<string> wordList,
        long startIndex,
        long endIndex,
        HashSet<string> targetHashes,
        int degreeOfParallelism,
        CancellationToken ct)
    {
        if (wordList.Count == 0 || endIndex < startIndex || startIndex >= wordList.Count)
            return [];

        endIndex = Math.Min(endIndex, wordList.Count - 1);

        var ranges = SplitRange(startIndex, endIndex, degreeOfParallelism);
        var results = new ConcurrentBag<(string Password, string Hash)>();

        Parallel.ForEach(
            ranges,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = degreeOfParallelism,
                CancellationToken = ct
            },
            range =>
            {
                for (long i = range.Start; i <= range.End; i++)
                {
                    if ((i & CancellationCheckMask) == 0)
                        ct.ThrowIfCancellationRequested();

                    var password = wordList[(int)i];
                    var hash = ComputeSha256(password);

                    if (targetHashes.Contains(hash))
                        results.Add((password, hash));
                }
            });

        return results.ToList();
    }

    private static List<(long Start, long End)> SplitRange(long startIndex, long endIndex, int parts)
    {
        var total = endIndex - startIndex + 1;
        var actualParts = (int)Math.Min(parts, total);
        var result = new List<(long Start, long End)>(actualParts);

        var baseSize = total / actualParts;
        var remainder = total % actualParts;

        long current = startIndex;

        for (int i = 0; i < actualParts; i++)
        {
            var size = baseSize + (i < remainder ? 1 : 0);
            var start = current;
            var end = current + size - 1;
            result.Add((start, end));
            current = end + 1;
        }

        return result;
    }

    private static (int length, long localIndex) GetLengthAndLocalIndex(string charset, int minLength, int maxLength, long absoluteIndex)
    {
        var index = absoluteIndex;
        for (var length = minLength; length <= maxLength; length++)
        {
            var countForLength = (long)Math.Pow(charset.Length, length);
            if (index < countForLength)
                return (length, index);

            index -= countForLength;
        }

        return (maxLength, 0);
    }

    private static string IndexToPassword(long index, string charset, int length)
    {
        var result = new char[length];
        var charsetLength = charset.Length;

        for (var i = length - 1; i >= 0; i--)
        {
            result[i] = charset[(int)(index % charsetLength)];
            index /= charsetLength;
        }

        return new string(result);
    }
}
