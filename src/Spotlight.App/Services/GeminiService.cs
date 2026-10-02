using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spotlight.App.Services;

public static class GeminiService
{
    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

    static GeminiService()
    {
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private static void Log(string msg) => AppPaths.Log("[search] " + msg);

    public static async Task<(string? Answer, string? Error)> AskGemini(string query)
    {
        Log($"=== AskGemini (First Result Mode) Started for: '{query}' ===");
        string searchUrl = $"https://websearch.miyami.tech/search-api?query={Uri.EscapeDataString(query)}&categories=general";

        try
        {
            Log($"Requesting search URL: {searchUrl}");
            var response = await _httpClient.GetAsync(searchUrl);
            Log($"Search response status: {response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                Log($"Search JSON received. Length: {json.Length}");
                var searchResponse = JsonSerializer.Deserialize<SearchResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (searchResponse?.Results != null && searchResponse.Results.Count > 0)
                {
                    foreach (var res in searchResponse.Results)
                    {
                        if (!string.IsNullOrEmpty(res.Content))
                        {
                            Log($"Returning first result content. Title: {res.Title}");
                            string formattedContent = $"{res.Title}\n\n{res.Content}";
                            return (formattedContent, null);
                        }
                    }
                }
                
                Log("No results found containing content.");
                return (null, "No search results found.");
            }
            else
            {
                Log($"Search request returned non-success code: {response.StatusCode}");
                return (null, $"Search API Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Log($"Search phase exception: {ex.GetType().Name} - {ex.Message}");
            return (null, $"Search Exception: {ex.Message}");
        }
    }

    private class SearchResponse
    {
        public string? Query { get; set; }
        public List<SearchResult>? Results { get; set; }
    }

    private class SearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }
}
