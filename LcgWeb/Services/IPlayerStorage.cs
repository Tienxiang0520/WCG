namespace LcgWeb.Services;

// Storage implementations preserve failed writes and reject an outdated browser snapshot.
public interface IPlayerStorage
{
    object Gate { get; }
    string? Read(string key);
    void Write(string key, string value, string? expected);
}
