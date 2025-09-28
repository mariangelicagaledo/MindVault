using System.Text;

namespace mindvault.Services;

public static class TempLog
{
    static readonly SemaphoreSlim _mutex = new(1, 1);
    static string? _cachedPath;

    // Fixed filename so it's easy to find each run
    public static string LogPath => _cachedPath ??= EnsurePath();

    public static string GetLogPath(string? _ = null) => LogPath;

    public static async Task ClearAsync()
    {
        await _mutex.WaitAsync();
        try
        {
            var path = LogPath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public static async Task AppendAsync(string message)
    {
        var line = $"[{DateTime.UtcNow:O}] {message}\n";
        await _mutex.WaitAsync();
        try
        {
            var path = LogPath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            await File.AppendAllTextAsync(path, line, Encoding.UTF8);
        }
        finally
        {
            _mutex.Release();
        }
    }

    static string EnsurePath()
    {
        string fileName = "ai_debug.log";
#if WINDOWS
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "MindVaultLogs", fileName);
#else
        // For Android/iOS/MacCatalyst use sandbox path; can be pulled via Device Explorer / Finder
        return Path.Combine(FileSystem.AppDataDirectory, fileName);
#endif
    }
}
