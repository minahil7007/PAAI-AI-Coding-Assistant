using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PAAI.Models;
using PAAI.Services;

namespace PAAI;

public partial class MainWindow : Window
{
    private readonly ClaudeService _claude = new();
    private readonly TerminalService _terminal = new();
    private readonly BrowserService _browser = new();
    private readonly DiffService _diffService = new();
    private readonly ObservableCollection<ChatMessage> _messages = new();
    private readonly List<string> _attachedFiles = new();
    private string _lastError = "";
    private string _lastErrorFile = "";
    private bool _isThinking = false;
    private bool _errorBeingFixed = false;

    public MainWindow()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _messages;

        AddAiMessage("Salam! Main PAAI hun — tumhara personal coding assistant.\n\nActive features:\n• Koi bhi error paste kar ke pooch sakte ho\n• '▶ Run' dabao — terminal errors detect honge\n• 'Watch' dabao — browser errors detect honge\n• Koi bhi file save karo — error detect karke fix karunga\n• 📎 Files attach karo\n• Shift+Enter new line, Enter send\n\nShuru karo!");

        _terminal.ErrorDetected += OnErrorDetected;
        _terminal.FileErrorDetected += OnFileErrorDetected;
        _browser.BrowserErrorDetected += OnErrorDetected;

        // CHANGED: poora userProfile watch karne ke bajaye, sirf
        // user ne jo folders pehle select kiye the wo load karo
        WatchedFoldersService.Load();
        if (WatchedFoldersService.Folders.Count > 0)
        {
            _terminal.RestartWatcherWithFolders(WatchedFoldersService.Folders);
            WatchedFoldersText.Text = $"{WatchedFoldersService.Folders.Count} folder(s) watch ho rahe hain";
        }
    }

    public void ShowAndPosition()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 10;
        Top = screen.Top + 10;
        Height = screen.Height - 20;
    }

    // ==================== FOLDER WATCHING (NAYA) ====================
    // NOTE: Microsoft.Win32.OpenFolderDialog use kiya hai (WPF-native, .NET 8+)
    // isse System.Windows.Forms enable karne ki zaroorat nahi padi — jo
    // Color/Button/Cursors/Brushes/MessageBox jaisi ambiguous reference
    // errors create kar raha tha poore project mein.
    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Project folder chuno jo watch karna hai"
        };

        if (dialog.ShowDialog() == true)
        {
            WatchedFoldersService.AddFolder(dialog.FolderName);
            _terminal.RestartWatcherWithFolders(WatchedFoldersService.Folders);
            WatchedFoldersText.Text = $"{WatchedFoldersService.Folders.Count} folder(s) watch ho rahe hain";
            AddAiMessage($"✅ Watch karna shuru: {dialog.FolderName}");
        }
    }

    // ==================== ERROR HANDLING ====================

    private void OnErrorDetected(string error)
    {
        Dispatcher.Invoke(() =>
        {
            if (_errorBeingFixed) return;
            _lastError = error;
            _lastErrorFile = "";
            ErrorPreview.Text = error.Length > 120
                ? error[..120] + "..." : error;
            ErrorBanner.Visibility = Visibility.Visible;
            _ = AutoAnalyzeError(error, "");
        });
    }

    private void OnFileErrorDetected(string error)
    {
        Dispatcher.Invoke(() =>
        {
            if (_errorBeingFixed) return;
            _lastError = error;
            var filePath = ExtractFilePath(error);
            _lastErrorFile = filePath;
            ErrorPreview.Text = error.Length > 120
                ? error[..120] + "..." : error;
            ErrorBanner.Visibility = Visibility.Visible;
            _ = AutoAnalyzeError(error, filePath);
        });
    }

    private async Task AutoAnalyzeError(string error, string filePath)
    {
        if (_errorBeingFixed) return;
        _errorBeingFixed = true;
        await Task.Delay(300);
        ErrorBanner.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            var contextLine = error.Split('\n').FirstOrDefault(l => l.StartsWith("📎"));
            var contextMsg = contextLine != null ? $"{contextLine}\n" : "";
            AddAiMessage($"Error detect hua! File: {Path.GetFileName(filePath)}\n{contextMsg}Fix kar raha hun...");
            await SmartFixFile(filePath, error);
        }
        else
        {
            AddAiMessage("Error detect hua! Analyze kar raha hun...");
            await GetAiResponse(
                $"Ye error analyze karo. Root cause batao aur complete fix do:\n\n{error}");
        }

        _errorBeingFixed = false;
    }

    private string ExtractFilePath(string error)
    {
        try
        {
            if (error.StartsWith("File: "))
            {
                var fileName = error.Split('\n')[0]
                    .Replace("File: ", "").Trim();

                // FIX: sirf un folders mein dhoondo jo user ne watch ke liye
                // select kiye hain — yeh hi jagah hai jahan file asal mein ho sakti hai
                foreach (var folder in WatchedFoldersService.Folders)
                {
                    try
                    {
                        if (!Directory.Exists(folder)) continue;
                        var files = Directory.GetFiles(folder, fileName,
                            SearchOption.AllDirectories);
                        if (files.Length > 0) return files.First();
                    }
                    catch { }
                }
            }
        }
        catch { }
        return "";
    }

    // ==================== SMART FIX ====================

    private async Task SmartFixFile(string filePath, string errorContext)
    {
        try
        {
            var originalCode = await File.ReadAllTextAsync(filePath);
            var fileName = Path.GetFileName(filePath);
            var ext = Path.GetExtension(filePath).ToLower();
            var lang = TerminalService.GetLanguage(ext);
            var projectFolder = Path.GetDirectoryName(filePath) ?? "";

            // Related files ka context
            var relatedContext = new StringBuilder();
            try
            {
                var relatedFiles = Directory.GetFiles(projectFolder, $"*{ext}")
                    .Where(f => f != filePath).Take(3);

                foreach (var relFile in relatedFiles)
                {
                    var relContent = await File.ReadAllTextAsync(relFile);
                    var preview = string.Join('\n',
                        relContent.Split('\n').Take(25));
                    relatedContext.AppendLine(
                        $"\n// {Path.GetFileName(relFile)}:\n{preview}");
                }
            }
            catch { }

            var prompt = $@"ERROR:
{errorContext}

FILE ({fileName}):
{originalCode}

{(relatedContext.Length > 0 ? $"RELATED FILES:\n{relatedContext}" : "")}

SIRF fixed {lang} code return karo — koi explanation nahi, koi backticks nahi, pure code only.";

            var requestBody = new
            {
                model = "claude-sonnet-4-6",
                max_tokens = 4000,
                system = $"Tu expert {lang} debugger hai. Error ki root cause dhundo, related context dekho, minimal aur correct fix karo. SIRF pure working code return karo — no backticks, no markdown, no explanation.",
                messages = new[] { new { role = "user", content = prompt } }
            };

            using var http = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", ConfigService.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8, "application/json");

            var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                AddAiMessage($"API error: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var fixedCode = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";

            fixedCode = fixedCode.Trim();
            if (fixedCode.StartsWith("```"))
            {
                var codeLines = fixedCode.Split('\n').ToList();
                codeLines.RemoveAt(0);
                if (codeLines.Count > 0 && codeLines.Last().Trim() == "```")
                    codeLines.RemoveAt(codeLines.Count - 1);
                fixedCode = string.Join('\n', codeLines);
            }

            if (string.IsNullOrWhiteSpace(fixedCode))
            {
                AddAiMessage("Fix nahi ho saka — dobara try karo.");
                return;
            }

            var diff = _diffService.GenerateDiff(
                filePath, originalCode, fixedCode, errorContext);

            AddAiMessage("Fix ready! Apply ya Ignore karo:");

            await Dispatcher.InvokeAsync(() =>
            {
                var diffWindow = new Views.DiffWindow(diff);
                diffWindow.Owner = this;
                diffWindow.ShowDialog();

                if (diffWindow.IsApplied)
                    AddAiMessage($"✅ {fileName} fix ho gayi!\n📁 Backup: {fileName}.paai_backup");
                else
                    AddAiMessage("Fix ignore kiya.");
            });
        }
        catch (Exception ex)
        {
            AddAiMessage($"Fix error: {ex.Message}");
        }
    }

    // ==================== ATTACHMENTS ====================

    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "File attach karo",
            Multiselect = true,
            Filter = "All Files|*.*|Code Files|*.cs;*.js;*.py;*.ts;*.html;*.css;*.java;*.cpp|Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
        };

        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            if (_attachedFiles.Contains(file)) continue;
            _attachedFiles.Add(file);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 4, 4),
                Margin = new Thickness(0, 0, 6, 0)
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var extIcon = Path.GetExtension(file).ToLower()
                is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"
                ? "🖼️" : "📄";

            panel.Children.Add(new TextBlock
            {
                Text = $"{extIcon} {Path.GetFileName(file)}",
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeBtn = new Button
            {
                Content = "✕",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                FontSize = 10,
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
                Tag = file
            };
            removeBtn.Click += RemoveAttachment_Click;
            panel.Children.Add(removeBtn);
            badge.Child = panel;
            AttachmentPanel.Children.Add(badge);
        }

        AttachmentScroll.Visibility = _attachedFiles.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string file)
        {
            _attachedFiles.Remove(file);
            var badge = (btn.Parent as StackPanel)?.Parent as Border;
            if (badge != null)
                AttachmentPanel.Children.Remove(badge);
        }
        AttachmentScroll.Visibility = _attachedFiles.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==================== BROWSER & RUN ====================

    private async void WatchBrowser_Click(object sender, RoutedEventArgs e)
    {
        var url = BrowserUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;
        AddAiMessage($"Browser watch ho raha hai: {url}");
        await _browser.StartWatchingAsync(url);
    }

    private async void StopBrowser_Click(object sender, RoutedEventArgs e)
    {
        await _browser.StopAsync();
        AddAiMessage("Browser watch band ho gaya.");
    }

    private async void RunProject_Click(object sender, RoutedEventArgs e)
    {
        var cmd = RunCommandBox.Text.Trim();
        if (string.IsNullOrEmpty(cmd)) return;
        ErrorBanner.Visibility = Visibility.Collapsed;

        var parts = cmd.Split(' ', 2);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : "";
        var workDir = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        AddAiMessage($"Running: {cmd}");
        try { _terminal.WatchProcess(command, args, workDir); }
        catch (Exception ex) { AddAiMessage($"Run error: {ex.Message}"); }
    }

    // ==================== SEND MESSAGE ====================

    private async void Send_Click(object sender, RoutedEventArgs e)
        => await SendMessage();

    private async void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            !Keyboard.IsKeyDown(Key.LeftShift) &&
            !Keyboard.IsKeyDown(Key.RightShift))
        {
            e.Handled = true;
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var text = InputBox.Text.Trim();
        if ((string.IsNullOrEmpty(text) && _attachedFiles.Count == 0)
            || _isThinking) return;

        InputBox.Text = "";
        var fullMessage = text;

        foreach (var file in _attachedFiles.ToList())
        {
            var ext = Path.GetExtension(file).ToLower();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")
                fullMessage += $"\n\n[Image: {Path.GetFileName(file)}]";
            else
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    var lang = TerminalService.GetLanguage(ext);
                    fullMessage += $"\n\n**File: {Path.GetFileName(file)}**\n```{lang}\n{content}\n```";
                }
                catch { }
            }
        }

        var displayText = string.IsNullOrEmpty(text)
            ? $"[{_attachedFiles.Count} file(s) attached]" : text;

        _attachedFiles.Clear();
        AttachmentPanel.Children.Clear();
        AttachmentScroll.Visibility = Visibility.Collapsed;

        AddUserMessage(displayText);
        await GetAiResponse(fullMessage);
    }

    // ==================== ERROR BANNER CLICK ====================

    private async void AskAboutError_Click(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrEmpty(_lastErrorFile) && File.Exists(_lastErrorFile))
            await SmartFixFile(_lastErrorFile, _lastError);
        else
            await GetAiResponse(
                $"Is error ko deeply analyze karo, root cause batao aur complete fix do:\n\n{_lastError}");
    }

    // ==================== AI RESPONSE ====================

    private async Task GetAiResponse(string userMessage, string? context = null)
    {
        _isThinking = true;
        var aiMsg = new ChatMessage { Text = "...", IsUser = false };
        _messages.Add(aiMsg);
        ScrollToBottom();

        var fullText = new StringBuilder();
        var index = _messages.Count - 1;

        await foreach (var chunk in _claude.AskAsync(userMessage, context))
        {
            fullText.Append(chunk);
            _messages[index] = new ChatMessage
            {
                Text = fullText.ToString(),
                IsUser = false
            };
            ScrollToBottom();
        }

        _isThinking = false;
    }

    private void AddUserMessage(string text)
    {
        _messages.Add(new ChatMessage { Text = text, IsUser = true });
        ScrollToBottom();
    }

    private void AddAiMessage(string text)
    {
        _messages.Add(new ChatMessage { Text = text, IsUser = false });
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(() => ChatScroll.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new Views.SettingsWindow();
        win.Owner = this;
        win.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosed(EventArgs e)
    {
        _terminal.Dispose();
        _ = _browser.StopAsync();
        base.OnClosed(e);
    }
}