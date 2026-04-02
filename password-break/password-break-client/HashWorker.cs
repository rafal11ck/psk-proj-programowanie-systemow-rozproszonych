using System.Security.Cryptography;
using System.Text;

namespace password_break_client;

public static class HashWorker
{
    public static string ComputeSha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static IEnumerable<(string Password, string Hash)> ProcessBruteForce(
        string charset, int minLength, int maxLength, long startIndex, long endIndex, HashSet<string> targetHashes)
    {
        var absoluteIndex = startIndex;
        var (length, localIndex) = GetLengthAndLocalIndex(charset, minLength, maxLength, startIndex);
        
        while (absoluteIndex <= endIndex && length <= maxLength)
        {
            var password = IndexToPassword(localIndex, charset, length);
            var hash = ComputeSha256(password);
            
            if (targetHashes.Contains(hash))
            {
                yield return (password, hash);
            }
            
            absoluteIndex++;
            localIndex++;
            
            var maxForLength = (long)Math.Pow(charset.Length, length);
            if (localIndex >= maxForLength)
            {
                localIndex = 0;
                length++;
            }
        }
    }

    private static (int length, long localIndex) GetLengthAndLocalIndex(string charset, int minLength, int maxLength, long absoluteIndex)
    {
        var index = absoluteIndex;
        for (var length = minLength; length <= maxLength; length++)
        {
            var countForLength = (long)Math.Pow(charset.Length, length);
            if (index < countForLength)
            {
                return (length, index);
            }
            index -= countForLength;
        }
        return (maxLength, 0);
    }

    public static IEnumerable<(string Password, string Hash)> ProcessDictionary(
        IReadOnlyList<string> wordList, long startIndex, long endIndex, HashSet<string> targetHashes)
    {
        for (var i = startIndex; i <= endIndex && i < wordList.Count; i++)
        {
            var password = wordList[(int)i];
            var hash = ComputeSha256(password);
            if (targetHashes.Contains(hash))
            {
                yield return (password, hash);
            }
        }
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