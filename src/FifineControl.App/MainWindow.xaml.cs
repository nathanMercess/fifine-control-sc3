using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using FifineControl.App.ViewModels;
using WinForms = System.Windows.Forms;

namespace FifineControl.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly WinForms.NotifyIcon trayIcon;
    private HwndSource? windowSource;
    private bool closingAfterRecordingStopped;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        ObsPasswordBox.Password = viewModel.ObsPassword;

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Abrir FifineControl", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Sair", null, (_, _) => Dispatcher.Invoke(Close));
        trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "FifineControl",
            Visible = true,
            ContextMenuStrip = menu
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(handle);
        windowSource?.AddHook(WindowMessageHook);
        viewModel.AttachGlobalHotkeys(handle);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && viewModel.MinimizeToTray)
        {
            Hide();
            trayIcon.ShowBalloonTip(1500, "FifineControl", "O controle continua disponível na bandeja.", WinForms.ToolTipIcon.Info);
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (viewModel.IsRecording && !closingAfterRecordingStopped)
        {
            var answer = System.Windows.MessageBox.Show(
                this,
                "Há uma gravação em andamento. Finalizar a gravação e sair?",
                "FifineControl",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            e.Cancel = true;
            if (answer == MessageBoxResult.Yes)
            {
                await viewModel.StopRecordingAsync();
                closingAfterRecordingStopped = true;
                Close();
            }
            return;
        }

        trayIcon.Visible = false;
        trayIcon.Dispose();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        windowSource?.RemoveHook(WindowMessageHook);
        windowSource = null;
        base.OnClosed(e);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = viewModel.ProcessWindowMessage(message, wParam);
        return IntPtr.Zero;
    }

    private void ObsPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        viewModel.ObsPassword = ObsPasswordBox.Password;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }
}
