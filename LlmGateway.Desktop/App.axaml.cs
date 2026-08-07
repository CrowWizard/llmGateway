using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LlmGateway.Desktop.Services;
using LlmGateway.Desktop.ViewModels;
using LlmGateway.Desktop.Views;

namespace LlmGateway.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppPaths();
            var gatewayHost = new GatewayHostService();
            var aililiAccountService = new AililiAccountService(paths);
            var viewModel = new MainWindowViewModel(
                paths,
                new GatewaySettingsService(paths),
                gatewayHost,
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
                new NodeRuntimeService(paths),
                aililiAccountService,
                new ErrorLogService(paths));

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += async (_, _) => await gatewayHost.StopAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
