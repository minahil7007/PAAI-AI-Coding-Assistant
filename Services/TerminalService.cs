using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PAAI.Services;

public class TerminalService
{
    public event Action<string>? ErrorDetected;
    public event Action<string>? FileErrorDetected;
    public static string? PaaiFixingFile { get; set; }
    private Process? _watchedProcess;
    private readonly HttpClient _http = new();

    // NAYA: Roslyn-based code index — cross-file dependencies aur syntax check ke liye
    private readonly CodeIndexService _codeIndex = new();

    // PAAI ka apna folder — ye watch nahi karega
    private static readonly string PaaiFolder =
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop", "PAAI");

    // ==================== multiple folder watching ====================
    private readonly List<FileSystemWatcher> _folderWatchers = new();

    // ==================== sensitive files kabhi Claude ko nahi jayengi ====================
    private static readonly string[] BlockedFileNames =
    { ".env", "appsettings.json", "appsettings.Development.json", "secrets.json", "config.json" };
    private static readonly string[] BlockedExtensions = { ".pem", ".key", ".pfx" };
    private static readonly string[] BlockedFolders =
    { "node_modules", "bin", "obj", ".git", "packages", "dist", "build" };

    private static readonly string[] SupportedExtensions =
    {
        "*.cs", "*.js", "*.jsx", "*.ts", "*.tsx", "*.py", "*.html",
        "*.css", "*.json", "*.java", "*.cpp", "*.c", "*.h", "*.hpp",
        "*.php", "*.rb", "*.go", "*.rs", "*.swift", "*.kt", "*.dart",
        "*.sql", "*.r", "*.bash", "*.sh", "*.scala", "*.pl", "*.lua",
        "*.hs", "*.ex", "*.exs", "*.clj", "*.vue", "*.svelte",
        "*.yml", "*.yaml", "*.xml", "*.md", "*.fs", "*.fsx", "*.vb",
        "*.groovy", "*.tf", "*.ps1", "*.psm1", "*.m", "*.mm",
        "*.zig", "*.nim", "*.cr", "*.erl", "*.ml", "*.pro",
        "*.cob", "*.f90", "*.f95", "*.for"
    };

    public static string GetLanguage(string ext) => ext switch
    {
        ".cs" => "C#",
        ".js" => "JavaScript",
        ".jsx" => "JavaScript React",
        ".ts" => "TypeScript",
        ".tsx" => "TypeScript React",
        ".py" => "Python",
        ".html" => "HTML",
        ".css" => "CSS",
        ".java" => "Java",
        ".cpp" => "C++",
        ".c" => "C",
        ".h" => "C/C++ Header",
        ".hpp" => "C++ Header",
        ".php" => "PHP",
        ".rb" => "Ruby",
        ".go" => "Go",
        ".rs" => "Rust",
        ".swift" => "Swift",
        ".kt" => "Kotlin",
        ".dart" => "Dart",
        ".sql" => "SQL",
        ".r" => "R",
        ".bash" => "Bash",
        ".sh" => "Shell",
        ".scala" => "Scala",
        ".pl" => "Perl",
        ".lua" => "Lua",
        ".hs" => "Haskell",
        ".ex" => "Elixir",
        ".exs" => "Elixir Script",
        ".clj" => "Clojure",
        ".vue" => "Vue",
        ".svelte" => "Svelte",
        ".yml" => "YAML",
        ".yaml" => "YAML",
        ".xml" => "XML",
        ".md" => "Markdown",
        ".fs" => "F#",
        ".fsx" => "F# Script",
        ".vb" => "Visual Basic",
        ".groovy" => "Groovy",
        ".tf" => "Terraform",
        ".ps1" => "PowerShell",
        ".psm1" => "PowerShell Module",
        ".json" => "JSON",
        ".m" => "Objective-C",
        ".mm" => "Objective-C++",
        ".zig" => "Zig",
        ".nim" => "Nim",
        ".cr" => "Crystal",
        ".erl" => "Erlang",
        ".ml" => "OCaml",
        ".pro" => "Prolog",
        ".cob" => "COBOL",
        ".f90" => "Fortran",
        ".f95" => "Fortran",
        ".for" => "Fortran",
        _ => "code"
    };

    // ==================== StartVSCodeWatcher() ki jagah yeh ====================
    public void RestartWatcherWithFolders(List<string> folders)
    {
        foreach (var w in _folderWatchers) w.Dispose();
        _folderWatchers.Clear();

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;

            // NAYA: poora folder Roslyn se index karo (class/method map banao)
            _codeIndex.IndexProject(folder);

            var watcher = new FileSystemWatcher
            {
                Path = folder,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            foreach (var ext in SupportedExtensions)
                watcher.Filters.Add(ext);

            watcher.Changed += OnFileChanged;
            _folderWatchers.Add(watcher);
        }
    }

    // ==================== sensitive files/folders check ====================
    private bool IsBlocked(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        var ext = Path.GetExtension(fullPath).ToLower();

        if (BlockedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)) return true;
        if (BlockedExtensions.Contains(ext)) return true;
        if (BlockedFolders.Any(bf => fullPath.Contains(
            $"{Path.DirectorySeparatorChar}{bf}{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase))) return true;

        return false;
    }

    private string _lastFile = "";
    private string _lastContent = "";

    // NAYA: per-file debounce timers — sirf tab check karo jab
    // user ne is file ko genuinely "chhod" diya ho (3 second koi change nahi)
    private readonly Dictionary<string, CancellationTokenSource> _debounceTokens = new();
    private const int DebounceMilliseconds = 3000; // 3 second idle wait

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // sensitive files ko yahin rok do
        if (IsBlocked(e.FullPath)) return;

        if (e.FullPath == PaaiFixingFile) return;

        // PAAI apna folder watch nahi karega
        if (e.FullPath.StartsWith(PaaiFolder,
            StringComparison.OrdinalIgnoreCase)) return;

        // Purana timer is file ke liye cancel karo (naya edit aaya, wait phir se shuru)
        lock (_debounceTokens)
        {
            if (_debounceTokens.TryGetValue(e.FullPath, out var oldCts))
                oldCts.Cancel();

            var cts = new CancellationTokenSource();
            _debounceTokens[e.FullPath] = cts;

            Task.Run(async () =>
            {
                try
                {
                    // User ke rukne ka wait — agar isi beech naya change aaya,
                    // yeh Task cancel ho jayega aur naya wait shuru hoga
                    await Task.Delay(DebounceMilliseconds, cts.Token);

                    // Yahan pahunche matlab 3 second se koi change nahi hua — genuinely ruk gaye
                    var content = File.ReadAllText(e.FullPath);
                    if (content == _lastContent) return;
                    _lastContent = content;

                    var errors = await CheckWithClaude(content, e.FullPath);
                    if (!string.IsNullOrEmpty(errors))
                        FileErrorDetected?.Invoke(
                            $"File: {Path.GetFileName(e.FullPath)}\n\n{errors}");
                }
                catch (TaskCanceledException)
                {
                    // Normal hai — user abhi bhi type kar raha tha, isliye cancel hua
                }
                catch { }
            }, cts.Token);
        }
    }

    private async Task<string> CheckWithClaude(string code, string filePath)
    {
        if (!ConfigService.IsConfigured) return "";

        var ext = Path.GetExtension(filePath).ToLower();
        var lang = GetLanguage(ext);

        // NAYA: Roslyn se pehle free/instant syntax check (sirf .cs files ke liye)
        if (ext == ".cs")
        {
            _codeIndex.IndexFile(filePath, code); // is file ko re-index karo (incremental)
            var syntaxErrors = _codeIndex.GetSyntaxErrors(code);
            if (syntaxErrors.Count > 0)
            {
                // Syntax error mil gaya — Claude ko bina call kiye seedha yeh return karo
                return "Syntax Errors:\n" + string.Join("\n", syntaxErrors);
            }
        }

        var lines = code.Split('\n');
        var limitedCode = string.Join('\n', lines.Take(100));

        // NAYA: sirf actual related files ka context (Roslyn se pata chala, guessing nahi)
        var relatedContext = new StringBuilder();
        var relatedFileNames = new List<string>();
        if (ext == ".cs")
        {
            var relatedFiles = _codeIndex.GetRelatedFiles(filePath);
            foreach (var rf in relatedFiles)
            {
                try
                {
                    var relContent = File.ReadAllText(rf);
                    var preview = string.Join('\n', relContent.Split('\n').Take(30));
                    relatedContext.AppendLine($"\n// {Path.GetFileName(rf)} (referenced):\n{preview}");
                    relatedFileNames.Add(Path.GetFileName(rf));
                }
                catch { }
            }
        }

        var prompt = $@"Ye {lang} code review karo — related files ke context ke sath agar diye gaye hain.
In sab cheezon ko check karo:
1. Syntax/type errors, undefined variables, missing brackets
2. Logic errors
3. Security issues: hardcoded passwords/API keys, SQL injection risk, unsafe input handling
Agar koi issue nahi hai to sirf likho: NO_ERRORS
Maximum 3 sabse important issues batao, short mein.

MAIN FILE:
{limitedCode}

{(relatedContext.Length > 0 ? $"RELATED FILES:\n{relatedContext}" : "")}";

        var requestBody = new
        {
            model = "claude-haiku-4-5",
            max_tokens = 300,
            system = "Tu ek code reviewer hai. Syntax, logic aur security teeno check karo, related files ka context bhi dekho. Sirf real issues batao, short mein. Agar koi issue nahi to NO_ERRORS likho.",
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", ConfigService.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return "";

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var result = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        if (result.Contains("NO_ERRORS")) return "";

        // NAYA: agar related files use hui hain, user ko transparently batao
        if (relatedFileNames.Count > 0)
            result = $"📎 Related context: {string.Join(", ", relatedFileNames)}\n\n{result}";

        return result;
    }

    public void WatchProcess(string command, string args, string workingDir)
    {
        StopWatching();

        _watchedProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        var errorBuffer = new System.Text.StringBuilder();

        _watchedProcess.ErrorDataReceived += (s, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            errorBuffer.AppendLine(e.Data);

            var data = e.Data.ToLower();
            if (data.Contains("error") || data.Contains("exception") ||
                data.Contains("failed") || data.Contains("unhandled"))
            {
                ErrorDetected?.Invoke(errorBuffer.ToString());
                errorBuffer.Clear();
            }
        };

        _watchedProcess.OutputDataReceived += (s, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var data = e.Data.ToLower();
            if (data.Contains("error") || data.Contains("exception") ||
                data.Contains("failed"))
                ErrorDetected?.Invoke(e.Data);
        };

        _watchedProcess.Start();
        _watchedProcess.BeginErrorReadLine();
        _watchedProcess.BeginOutputReadLine();
    }

    public void StopWatching()
    {
        try { _watchedProcess?.Kill(true); } catch { }
        _watchedProcess = null;
    }

    public void Dispose()
    {
        StopWatching();
        foreach (var w in _folderWatchers) w.Dispose();
    }
}