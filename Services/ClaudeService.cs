using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace PAAI.Services;

public class ClaudeService
{
    private readonly HttpClient _http = new();
    private readonly List<object> _history = new();
    private readonly string _historyFile;

    private const string SystemPrompt = @"Tu PAAI hai — Personal AI Assistant for developers.
Tu ek experienced senior developer ki tarah kaam karta hai jo beginners ke saath baith ke code sikhata hai.
Rules:
1. Hamesha simple, beginner-friendly language mein samjhao
2. Pehle KYON error aya ye batao, phir fix batao
3. Agar user Urdu/Roman Urdu mein pooche to Urdu/Roman Urdu mein jawab do
4. Agar English mein pooche to English mein jawab do
5. Jab code fix karo to SIRF changed lines dikhao
6. Har fix ke baad ek short tip do
7. Kabhi judge mat karo
8. Conversation history yaad rakho — user ne pehle kya pucha wo context mein rakho";

    public ClaudeService()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        var paaiFolder = Path.Combine(appData, "PAAI");
        Directory.CreateDirectory(paaiFolder);
        _historyFile = Path.Combine(paaiFolder, "chat_history.json");
        LoadHistory();
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyFile)) return;
            var json = File.ReadAllText(_historyFile);
            var loaded = JsonSerializer.Deserialize<List<HistoryMessage>>(json);
            if (loaded == null) return;
            foreach (var msg in loaded.TakeLast(50))
                _history.Add(new { role = msg.Role, content = msg.Content });
        }
        catch { }
    }

    private void SaveHistory()
    {
        try
        {
            var toSave = new List<HistoryMessage>();
            foreach (var item in _history.TakeLast(50))
            {
                var json = JsonSerializer.Serialize(item);
                var doc = JsonDocument.Parse(json);
                toSave.Add(new HistoryMessage
                {
                    Role = doc.RootElement.GetProperty("role").GetString() ?? "",
                    Content = doc.RootElement.GetProperty("content").GetString() ?? ""
                });
            }
            File.WriteAllText(_historyFile,
                JsonSerializer.Serialize(toSave,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public async IAsyncEnumerable<string> AskAsync(
        string userMessage,
        string? codeContext = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!ConfigService.IsConfigured)
        {
            yield return "API key nahi hai. Settings mein Claude API key daalo.";
            yield break;
        }

        var content = codeContext != null
            ? $"{userMessage}\n\n```\n{codeContext}\n```"
            : userMessage;

        _history.Add(new { role = "user", content });

        var requestBody = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 1024,
            system = SystemPrompt,
            messages = _history,
            stream = true
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", ConfigService.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        bool requestFailed = false;
        string failMessage = "";

        try
        {
            response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                failMessage = $"API error: {response.StatusCode} - {err}";
                requestFailed = true;
            }
        }
        catch (Exception ex)
        {
            failMessage = $"Connection error: {ex.Message}";
            requestFailed = true;
        }

        if (requestFailed)
        {
            _history.RemoveAt(_history.Count - 1);
            yield return failMessage;
            yield break;
        }

        var fullResponse = new StringBuilder();
        using var stream = await response!.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string chunk = "";
            bool parsed = false;
            try
            {
                var doc = JsonDocument.Parse(data);
                var type = doc.RootElement.GetProperty("type").GetString();
                if (type == "content_block_delta")
                {
                    var delta = doc.RootElement.GetProperty("delta");
                    if (delta.GetProperty("type").GetString() == "text_delta")
                    {
                        chunk = delta.GetProperty("text").GetString() ?? "";
                        parsed = true;
                    }
                }
            }
            catch { }

            if (parsed && !string.IsNullOrEmpty(chunk))
            {
                fullResponse.Append(chunk);
                yield return chunk;
            }
        }

        if (fullResponse.Length > 0)
        {
            _history.Add(new { role = "assistant", content = fullResponse.ToString() });
            SaveHistory();
        }
    }

    public void ClearHistory()
    {
        _history.Clear();
        try { File.Delete(_historyFile); } catch { }
    }
}

public class HistoryMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}