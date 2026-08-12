using System.IO;
using System.Windows;
using PAAI.Models;

namespace PAAI.Views;

public partial class DiffWindow : Window
{
    private readonly DiffResult _diff;
    public bool IsApplied { get; private set; } = false;

    public DiffWindow(DiffResult diff)
    {
        InitializeComponent();
        _diff = diff;

        FileNameText.Text = $"File: {diff.FileName}";
        ErrorText.Text = diff.ErrorContext.Length > 200
            ? diff.ErrorContext[..200] + "..."
            : diff.ErrorContext;

        DiffList.ItemsSource = diff.Lines;

        var added = diff.Lines.Count(l => l.Type == DiffLineType.Added);
        var removed = diff.Lines.Count(l => l.Type == DiffLineType.Removed);
        SummaryText.Text = $"+{added} lines aayengi   −{removed} lines hatengi";
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Backup banao
            var backupPath = _diff.FilePath + ".paai_backup";
            await File.WriteAllTextAsync(backupPath, _diff.OriginalCode);

            // Fixed code save karo
            await File.WriteAllTextAsync(_diff.FilePath, _diff.FixedCode);

            IsApplied = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"File save nahi ho saki: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Ignore_Click(object sender, RoutedEventArgs e)
    {
        IsApplied = false;
        DialogResult = false;
        Close();
    }
}