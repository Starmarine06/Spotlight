using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Spotlight.App.Services;

public class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime DateModified { get; set; }
    public string Extension { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public int Score { get; set; }
}

/// <summary>
/// In-memory index of the user's files and folders. Replaces the old per-keystroke `fd.exe` process: lookups are
/// answered from RAM in a few milliseconds, results are ranked (not just filtered), folders are included, and a
/// FileSystemWatcher keeps the index fresh between periodic full rescans.
/// </summary>
public class FileIndex : IDisposable
{
    private const int MaxEntries = 800_000;

    private struct Entry
    {
        public string Name;
        public int Dir;
        public long Size;
        public long Ticks;
        public bool IsDir;
    }

    private sealed class Snapshot
    {
        public Entry[] Entries = Array.Empty<Entry>();
        public string[] Dirs = Array.Empty<string>();
    }

    private static readonly HashSet<string> NoisyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".sys", ".tmp", ".log", ".map", ".pdb", ".obj", ".lock", ".cache", ".db", ".dat", ".bin",
        ".mui", ".manifest", ".etl", ".ini", ".pyc", ".class", ".o", ".d", ".lib", ".exp", ".idx", ".pack"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".csv", ".rtf", ".odt",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp3", ".mp4", ".mkv", ".zip", ".psd", ".fig", ".sketch"
    };

    private volatile Snapshot _snapshot = new();
    private readonly object _deltaLock = new();
    private readonly List<(Entry Entry, string Dir)> _added = new();
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly string[] _preferredRoots;
    private HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private bool _indexDotFolders;
    private Timer? _rescanTimer;
    private int _building;
    private string[] _roots = Array.Empty<string>();

    public bool Ready { get; private set; }
    public int Count => _snapshot.Entries.Length;
    public event Action? IndexChanged;

    public FileIndex()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _preferredRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            System.IO.Path.Combine(profile, "Downloads"),
        };
    }

    public void Start()
    {
        _ = Task.Run(Build);
        // Safety net in case the watchers miss events (buffer overflow, network drives, ...).
        _rescanTimer = new Timer(_ => _ = Task.Run(Build), null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    private void Build()
    {
        if (Interlocked.Exchange(ref _building, 1) == 1) return;

        try
        {
            var sw = Stopwatch.StartNew();
            var settings = Settings.Current;
            _excluded = new HashSet<string>(settings.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
            _indexDotFolders = settings.IndexDotFolders;

            var roots = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            roots.AddRange(settings.ExtraIndexRoots.Where(Directory.Exists));
            _roots = roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var entries = new List<Entry>(200_000);
            var dirs = new List<string>(20_000);

            foreach (var root in _roots)
                Crawl(root, entries, dirs);

            var snapshot = new Snapshot { Entries = entries.ToArray(), Dirs = dirs.ToArray() };
            lock (_deltaLock)
            {
                _snapshot = snapshot;
                _added.Clear();
                _removed.Clear();
            }

            Ready = true;
            AppPaths.Log($"File index built: {snapshot.Entries.Length} entries in {sw.ElapsedMilliseconds} ms");
            SetupWatchers();
            IndexChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppPaths.Log("File index build failed: " + ex);
        }
        finally
        {
            Interlocked.Exchange(ref _building, 0);
        }
    }

    private void Crawl(string root, List<Entry> entries, List<string> dirs)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0 && entries.Count < MaxEntries)
        {
            var dir = stack.Pop();
            int dirIndex = dirs.Count;
            dirs.Add(dir);

            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System
                });
            }
            catch { continue; }

            try
            {
                foreach (var info in children)
                {
                    bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junctions/symlinks => loops

                    if (isDir)
                    {
                        if (_excluded.Contains(info.Name)) continue;
                        if (!_indexDotFolders && info.Name.StartsWith('.')) continue;
                        stack.Push(info.FullName);
                    }

                    entries.Add(new Entry
                    {
                        Name = info.Name,
                        Dir = dirIndex,
                        IsDir = isDir,
                        Size = isDir ? 0 : ((FileInfo)info).Length,
                        Ticks = info.LastWriteTimeUtc.Ticks
                    });
                }
            }
            catch { /* directory vanished or became inaccessible mid-enumeration */ }
        }
    }

    // ---------------------------------------------------------------- live updates

    private void SetupWatchers()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();

        foreach (var root in _roots)
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024
                };
                watcher.Created += (_, e) => OnCreated(e.FullPath);
                watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
                watcher.Renamed += (_, e) => { OnDeleted(e.OldFullPath); OnCreated(e.FullPath); };
                watcher.Error += (_, _) => _ = Task.Run(Build);
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                AppPaths.Log($"Watcher failed for {root}: {ex.Message}");
            }
        }
    }

    private bool IsExcludedPath(string path)
    {
        var segments = path.Split(System.IO.Path.DirectorySeparatorChar);
        for (int i = 0; i < segments.Length - 1; i++) // the last segment is the item itself
        {
            var segment = segments[i];
            if (_excluded.Contains(segment) || (!_indexDotFolders && segment.Length > 1 && segment[0] == '.')) return true;
        }
        return false;
    }

    private void OnCreated(string fullPath)
    {
        try
        {
            if (IsExcludedPath(fullPath)) return;
            FileSystemInfo info = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
            if (!info.Exists) return;
            bool isDir = info is DirectoryInfo;

            lock (_deltaLock)
            {
                _removed.Remove(fullPath);
                if (_added.Count > 50_000) return; // a full rescan will pick it up
                _added.Add((new Entry
                {
                    Name = info.Name,
                    IsDir = isDir,
                    Size = isDir ? 0 : ((FileInfo)info).Length,
                    Ticks = info.LastWriteTimeUtc.Ticks
                }, System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty));
            }
        }
        catch { }
    }

    private void OnDeleted(string fullPath)
    {
        lock (_deltaLock)
        {
            _removed.Add(fullPath);
            _added.RemoveAll(a => string.Equals(System.IO.Path.Combine(a.Dir, a.Entry.Name), fullPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---------------------------------------------------------------- search

    public List<FileItem> Search(string query, int limit = 60, bool includeFolders = true)
    {
        var results = new List<FileItem>();
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return results;

        // Operators: ext:pdf  type:folder|file
        var tokens = new List<string>();
        string? extFilter = null;
        bool? foldersOnly = null;
        foreach (var raw in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith("ext:", StringComparison.OrdinalIgnoreCase) && raw.Length > 4)
                extFilter = "." + raw[4..].TrimStart('.');
            else if (raw.Equals("type:folder", StringComparison.OrdinalIgnoreCase) || raw.Equals("folder:", StringComparison.OrdinalIgnoreCase))
                foldersOnly = true;
            else if (raw.Equals("type:file", StringComparison.OrdinalIgnoreCase))
                foldersOnly = false;
            else
                tokens.Add(raw);
        }

        if (tokens.Count == 0 && extFilter == null && foldersOnly == null) return results;

        var snap = _snapshot;
        var scored = new List<(int Score, int Index, int DeltaIndex)>(1024);
        var now = DateTime.UtcNow.Ticks;

        // Longest token is the most selective, so check it first.
        var ordered = tokens.OrderByDescending(t => t.Length).ToArray();

        for (int i = 0; i < snap.Entries.Length; i++)
        {
            ref readonly var e = ref snap.Entries[i];
            if (!includeFolders && e.IsDir) continue;
            if (foldersOnly == true && !e.IsDir) continue;
            if (foldersOnly == false && e.IsDir) continue;

            int score = Score(e, snap.Dirs[e.Dir], ordered, extFilter, now);
            if (score > 0) scored.Add((score, i, -1));
        }

        List<(Entry Entry, string Dir)> added;
        HashSet<string> removed;
        lock (_deltaLock)
        {
            added = new List<(Entry, string)>(_added);
            removed = new HashSet<string>(_removed, StringComparer.OrdinalIgnoreCase);
        }

        for (int i = 0; i < added.Count; i++)
        {
            var e = added[i].Entry;
            if (!includeFolders && e.IsDir) continue;
            if (foldersOnly == true && !e.IsDir) continue;
            if (foldersOnly == false && e.IsDir) continue;
            int score = Score(e, added[i].Dir, ordered, extFilter, now);
            if (score > 0) scored.Add((score, -1, i));
        }

        // Typo / abbreviation fallback: if substring matching found almost nothing, try subsequence matching.
        if (scored.Count < 5 && tokens.Count == 1 && tokens[0].Length >= 3)
        {
            var q = tokens[0];
            for (int i = 0; i < snap.Entries.Length; i++)
            {
                ref readonly var e = ref snap.Entries[i];
                if (!includeFolders && e.IsDir) continue;
                if (foldersOnly == true && !e.IsDir) continue;
                if (foldersOnly == false && e.IsDir) continue;
                if (extFilter != null && !e.Name.EndsWith(extFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsSubsequence(q, e.Name))
                    scored.Add((300 + Recency(e.Ticks, now) - Math.Min(e.Name.Length, 100), i, -1));
            }
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        foreach (var (score, index, deltaIndex) in scored)
        {
            if (results.Count >= limit) break;

            Entry e;
            string dir;
            if (index >= 0) { e = snap.Entries[index]; dir = snap.Dirs[e.Dir]; }
            else { e = added[deltaIndex].Entry; dir = added[deltaIndex].Dir; }

            var path = System.IO.Path.Combine(dir, e.Name);
            if (removed.Count > 0 && removed.Contains(path)) continue;

            results.Add(new FileItem
            {
                Name = e.Name,
                Path = path,
                IsFolder = e.IsDir,
                Size = e.Size,
                DateModified = new DateTime(e.Ticks, DateTimeKind.Utc).ToLocalTime(),
                Extension = e.IsDir ? string.Empty : System.IO.Path.GetExtension(e.Name),
                Score = score
            });
        }

        return results;
    }

    /// <summary>Most recently modified user documents; shown when the files filter is opened with no query.</summary>
    public List<FileItem> Recent(int limit = 40)
    {
        var snap = _snapshot;
        var heap = new PriorityQueue<int, long>(); // min-heap on modification time
        for (int i = 0; i < snap.Entries.Length; i++)
        {
            ref readonly var e = ref snap.Entries[i];
            if (e.IsDir) continue;
            var ext = System.IO.Path.GetExtension(e.Name);
            if (!DocumentExtensions.Contains(ext)) continue;
            if (heap.Count < limit) heap.Enqueue(i, e.Ticks);
            else if (heap.TryPeek(out _, out var oldest) && e.Ticks > oldest)
            {
                heap.Dequeue();
                heap.Enqueue(i, e.Ticks);
            }
        }

        var list = new List<FileItem>();
        while (heap.Count > 0)
        {
            var e = snap.Entries[heap.Dequeue()];
            list.Add(new FileItem
            {
                Name = e.Name,
                Path = System.IO.Path.Combine(snap.Dirs[e.Dir], e.Name),
                Size = e.Size,
                DateModified = new DateTime(e.Ticks, DateTimeKind.Utc).ToLocalTime(),
                Extension = System.IO.Path.GetExtension(e.Name)
            });
        }
        list.Reverse();
        return list;
    }

    private int Score(in Entry e, string dir, string[] tokens, string? extFilter, long nowTicks)
    {
        if (extFilter != null && !e.Name.EndsWith(extFilter, StringComparison.OrdinalIgnoreCase)) return 0;

        var name = e.Name;
        int dot = e.IsDir ? -1 : name.LastIndexOf('.');
        int baseLen = dot > 0 ? dot : name.Length;

        if (tokens.Length == 0)
            return 1000 + Recency(e.Ticks, nowTicks);

        double total = 0;
        foreach (var token in tokens)
        {
            double s;
            int idx = name.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx == 0) s = 1.0;
            else if (idx > 0 && IsBoundary(name[idx - 1])) s = 0.8;
            else if (idx > 0) s = 0.5;
            else if (tokens.Length > 1 && dir.Contains(token, StringComparison.OrdinalIgnoreCase)) s = 0.25;
            else return 0; // every token must match something

            total += s;
        }

        int score = (int)(total / tokens.Length * 5000);

        // Exact base-name match is far better than a prefix match.
        if (tokens.Length == 1 && baseLen == tokens[0].Length && name.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase))
            score += 4000;

        score += Math.Max(0, 300 - baseLen * 3);          // shorter names are more specific
        score += Recency(e.Ticks, nowTicks);

        if (!e.IsDir)
        {
            var ext = dot > 0 ? name[dot..] : string.Empty;
            if (NoisyExtensions.Contains(ext)) score -= 1500;
            else if (DocumentExtensions.Contains(ext)) score += 120;
        }
        else
        {
            score += 60;
        }

        foreach (var preferred in _preferredRoots)
        {
            if (dir.StartsWith(preferred, StringComparison.OrdinalIgnoreCase)) { score += 150; break; }
        }

        // Deeply nested files are usually build output / vendored content.
        int depth = 0;
        foreach (var c in dir) if (c == '\\') depth++;
        score -= Math.Min(depth, 12) * 12;

        return Math.Max(score, 1);
    }

    private static int Recency(long ticks, long nowTicks)
    {
        var days = (nowTicks - ticks) / (double)TimeSpan.TicksPerDay;
        if (days < 1) return 220;
        if (days < 7) return 160;
        if (days < 30) return 90;
        if (days < 180) return 30;
        return 0;
    }

    private static bool IsBoundary(char c) => c is ' ' or '-' or '_' or '.' or '(' or '[' or ')' or ']';

    private static bool IsSubsequence(string query, string text)
    {
        int qi = 0;
        for (int i = 0; i < text.Length && qi < query.Length; i++)
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(query[qi])) qi++;
        return qi == query.Length;
    }

    public void Dispose()
    {
        _rescanTimer?.Dispose();
        foreach (var w in _watchers) w.Dispose();
    }
}
