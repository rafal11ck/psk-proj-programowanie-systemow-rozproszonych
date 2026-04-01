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
        string charset, int length, long startIndex, long endIndex)
    {
        var charsetArray = charset.ToCharArray();
        
        for (var i = startIndex; i <= endIndex; i++)
        {
            var password = IndexToPassword(i, charsetArray, length);
            var hash = ComputeSha256(password);
            yield return (password, hash);
        }
    }

    public static IEnumerable<(string Password, string Hash)> ProcessWords(IEnumerable<string> words)
    {
        foreach (var word in words)
        {
            var hash = ComputeSha256(word);
            yield return (word, hash);
        }
    }

    private static string IndexToPassword(long index, char[] charset, int length)
    {
        var result = new char[length];
        var charsetLength = charset.Length;
        
        for (var i = length - 1; i >= 0; i--)
        {
            result[i] = charset[index % charsetLength];
            index /= charsetLength;
        }
        
        return new string(result);
    }
}