using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using PAAI.Views;
using PAAI.Services;

namespace PAAI;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private HwndSource? _hwndSource;
    private const int HOTKEY_ID = 9000;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_SPACE = 0x20;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigService.Load();
        _mainWindow = new MainWindow();

        _mainWindow.Loaded += (s, args) =>
        {
            var helper = new WindowInteropHelper(_mainWindow);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource.AddHook(HwndHook);
            RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL, VK_SPACE);
        };

        _mainWindow.ShowAndPosition();
        _mainWindow.Show();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ToggleWindow()
    {
        if (_mainWindow == null) return;
        if (_mainWindow.IsVisible)
            _mainWindow.Hide();
        else
        {
            _mainWindow.ShowAndPosition();
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainWindow != null)
        {
            var helper = new WindowInteropHelper(_mainWindow);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
        }
        _hwndSource?.Dispose();
        base.OnExit(e);
    }
}