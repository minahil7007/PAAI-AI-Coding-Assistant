using System.Windows;
using PAAI.Services;

namespace PAAI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        if (ConfigService.IsConfigured)
            ApiKeyBox.Password = ConfigService.ApiKey;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            StatusText.Text = "API key khali nahi honi chahiye!";
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            StatusText.Visibility = Visibility.Visible;
            return;
        }
        ConfigService.Save(key);
        StatusText.Text = "Saved!";
        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        StatusText.Visibility = Visibility.Visible;
        Task.Delay(800).ContinueWith(_ => Dispatcher.Invoke(Close));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}