using System.Collections.Concurrent;
namespace LcgWeb.Services;

public sealed class FilePlayerStorage : IPlayerStorage
{
    private static readonly ConcurrentDictionary<string, object> Gates = new();
    private readonly IReadOnlyDictionary<string, string> paths;
    public object Gate { get; }
    public FilePlayerStorage(IReadOnlyDictionary<string, string> paths)
    {
        this.paths = paths.ToDictionary(p => p.Key, p => System.IO.Path.GetFullPath(p.Value));
        Gate = Gates.GetOrAdd(string.Join("|", this.paths.Values.Order()), _ => new());
    }
    public string? Read(string key) => File.Exists(paths[key]) ? File.ReadAllText(paths[key]) : null;
    public void Write(string key, string value, string? expected)
    {
        var path = paths[key];
        lock (Gate)
        {
            if (Read(key) != expected) throw new IOException("存檔已更新，請重新載入後再操作。");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".decks-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) { writer.Write(value); writer.Flush(); stream.Flush(true); }
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
