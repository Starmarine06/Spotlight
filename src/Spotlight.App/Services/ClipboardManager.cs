using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Spotlight.App.Services;

public class ClipItem
{
    public string FullText { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}

public static class ClipboardManager
{
    private static readonly List<ClipItem> _history = new();
    private static readonly string _historyPath = Path.Combine(AppPaths.DataDir, "clipboard_history.json");
    private static readonly string _legacyPath = Path.Combine(AppPaths.InstallDir, "clipboard_history.json");

    static ClipboardManager()
    {
        LoadHistory();
    }

    public static List<ClipItem> GetHistory()
    {
        lock (_history)
        {
            return new List<ClipItem>(_history);
        }
    }

    public static void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        
        // Prevent storing ridiculously large clipboard entries to protect performance
        if (text.Length > 8000) return;

        lock (_history)
        {
            // Remove previous identical entry to bubble it to the top
            _history.RemoveAll(c => c.FullText.Equals(text, StringComparison.Ordinal));

            _history.Insert(0, new ClipItem
            {
                FullText = text,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            // Cap the total entries to 50
            if (_history.Count > 50)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            SaveHistory();
        }
    }

    public static void Delete(string text)
    {
        lock (_history)
        {
            _history.RemoveAll(c => c.FullText.Equals(text, StringComparison.Ordinal));
            SaveHistory();
        }
    }

    public static void Clear()
    {
        lock (_history)
        {
            _history.Clear();
            SaveHistory();
        }
    }

    private static void LoadHistory()
    {
        try
        {
            // History used to live next to the executable; adopt it so updates never lose it.
            if (!File.Exists(_historyPath) && File.Exists(_legacyPath))
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.Move(_legacyPath, _historyPath);
            }

            if (File.Exists(_historyPath))
            {
                var json = File.ReadAllText(_historyPath);
                var list = JsonSerializer.Deserialize<List<ClipItem>>(json);
                if (list != null)
                {
                    _history.AddRange(list);
                }
            }
        }
        catch
        {
            // Ignore corrupted json loads
        }
    }

    private static void SaveHistory()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_history, options);
            File.WriteAllText(_historyPath, json);
        }
        catch
        {
            // Ignore saving faults
        }
    }
}
