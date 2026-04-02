namespace password_break_server.Services;

public interface IFoundPasswords
{
    event Action? OnAllFound;
    void StoreFound(IEnumerable<(string Password, string Hash)> entries);
    bool AllFound { get; }
    bool Saved { get; }
    int FoundCount { get; }
    int RemainingCount { get; }
    IReadOnlyDictionary<string, string> GetAllFound();
    void SaveToFile(string filePath);
}
