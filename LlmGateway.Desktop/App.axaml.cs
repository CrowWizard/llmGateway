using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LlmGateway.Desktop.Services;
using LlmGateway.Desktop.ViewModels;
using LlmGateway.Desktop.Views;

namespace LlmGateway.Desktop;

public sealed partial class App : Application
{
    private GatewayServiceManager? _gatewayServiceManager;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var errorLogService = new ErrorLogService(paths);
            _gatewayServiceManager = new GatewayServiceManager(paths);
            var viewModel = new MainWindowViewModel(
                paths,
                new GatewaySettingsService(paths),
                _gatewayServiceManager,
                new CodexConfigService(paths),
                new CodexAuthService(paths),
                new CodexBackupService(paths),
                new CodexStateService(paths),
                new EnvironmentVariableService(paths),
                new EcommerceImageStudioSkillService(paths),
                new CodexSkillService(paths),
                new ModelService(),
                new ApplicationLauncher(),
                new CodexLocalizationService(),
                new NodeRuntimeService(paths, errorLogService: errorLogService),
                errorLogService);

            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;
            mainWindow.Closing += (_, eventArgs) =>
            {
                if (mainWindow.IsExitRequested)
                {
                    return;
                }

                eventArgs.Cancel = true;
                mainWindow.Hide();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static void ShowMainWindow()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } window)
        {
            window.Show();
            window.WindowState = Avalonia.Controls.WindowState.Normal;
            window.Activate();
        }
    }

    internal static void ExitDesktop()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow window)
        {
            window.IsExitRequested = true;
            desktop.Shutdown();
        }
    }

    private void OnTrayIconClick(object? sender, EventArgs eventArgs) => ShowMainWindow();

    private void OnShowWindowClick(object? sender, EventArgs eventArgs) => ShowMainWindow();

    private void OnExitDesktopClick(object? sender, EventArgs eventArgs) => ExitDesktop();

    private async void OnInstallGatewayServiceClick(object? sender, EventArgs eventArgs) =>
        await RunTrayServiceActionAsync(manager => manager.InstallAsync(), "Windows 服务已安装。");

    private async void OnStartGatewayServiceClick(object? sender, EventArgs eventArgs) =>
        await RunTrayServiceActionAsync(manager => manager.StartAsync(), "Windows 服务已启动。");

    private async void OnStopGatewayServiceClick(object? sender, EventArgs eventArgs) =>
        await RunTrayServiceActionAsync(manager => manager.StopAsync(), "Windows 服务已停止。");

    private async void OnUninstallGatewayServiceClick(object? sender, EventArgs eventArgs) =>
        await RunTrayServiceActionAsync(manager => manager.UninstallAsync(), "Windows 服务已卸载。");

    private async Task RunTrayServiceActionAsync(Func<GatewayServiceManager, Task> action, string successMessage)
    {
        if (_gatewayServiceManager is null)
        {
            return;
        }

        try
        {
            await action(_gatewayServiceManager);
            SetGatewayStatus(successMessage);
        }
        catch (Exception exception)
        {
            SetGatewayStatus($"服务操作失败：{exception.Message}");
        }
    }

    private static void SetGatewayStatus(string status)
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel viewModel })
        {
            viewModel.UpdateGatewayStatus(status);
        }
    }
}
