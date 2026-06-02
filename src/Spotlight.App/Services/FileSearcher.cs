using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Spotlight.App.Services;

public class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime DateModified { get; set; }
    public string Extension { get; set; } = string.Empty;
}

public class FileSearcher
{
    public List<FileItem> SearchFiles(string query, int limit = 50)
    {
        var results = new List<FileItem>();
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return results;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        try
        {
            var localFdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fd.exe");
            var fdExecutable = File.Exists(localFdPath) ? localFdPath : "fd";

            var startInfo = new ProcessStartInfo
            {
                FileName = fdExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--type");
            startInfo.ArgumentList.Add("f");
            startInfo.ArgumentList.Add("--exclude");
            startInfo.ArgumentList.Add("AppData");
            startInfo.ArgumentList.Add("--exclude");
            startInfo.ArgumentList.Add("node_modules");
            startInfo.ArgumentList.Add("--exclude");
            startInfo.ArgumentList.Add(".git");
            startInfo.ArgumentList.Add("--max-results");
            startInfo.ArgumentList.Add(limit.ToString());
            startInfo.ArgumentList.Add("--color");
            startInfo.ArgumentList.Add("never");
            startInfo.ArgumentList.Add(query);
            startInfo.ArgumentList.Add(userProfile);

            using var process = Process.Start(startInfo);
            if (process == null) return results;

            using var reader = process.StandardOutput;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var filePath = line.Trim();
                if (string.IsNullOrEmpty(filePath)) continue;

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        results.Add(new FileItem
                        {
                            Name = fileInfo.Name,
                            Path = filePath,
                            Size = fileInfo.Length,
                            DateModified = fileInfo.LastWriteTime,
                            Extension = fileInfo.Extension
                        });
                    }
                    else
                    {
                        results.Add(new FileItem
                        {
                            Name = System.IO.Path.GetFileName(filePath),
                            Path = filePath,
                            Extension = System.IO.Path.GetExtension(filePath)
                        });
                    }
                }
                catch
                {
                    results.Add(new FileItem
                    {
                        Name = System.IO.Path.GetFileName(filePath),
                        Path = filePath,
                        Extension = System.IO.Path.GetExtension(filePath)
                    });
                }
            }

            process.WaitForExit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("fd Searcher Error: " + ex.Message);
        }

        return results;
    }
}
