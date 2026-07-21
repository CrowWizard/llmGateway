using System.Collections.ObjectModel;
using Avalonia.Threading;
using LlmGateway.Desktop.Infrastructure;
using LlmGateway.Desktop.Models;
using LlmGateway.Desktop.Services;

namespace LlmGateway.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly GatewaySettingsService _gatewaySettingsService;
    private readonly GatewayHostService _gatewayHost;
    private readonly CodexConfigService _codexConfig;
    private readonly CodexAuthService _codexAuth;
    private readonly CodexBackupService _backupService;
    private readonly EnvironmentVariableService _environmentService;
    private readonly ModelService _modelService;
    private readonly ApplicationLauncher _launcher;
    private readonly AsyncCommand _startGatewayCommand;
    private readonly AsyncCommand _stopGatewayCommand;

    private string _localBindIp = "127.0.0.1";
    private int _listenPort = 23001;
    private string _upstreamBaseUrl = string.Empty;
    private string _userAgent = string.Empty;
    private bool _overwriteUserAgent;
    private bool _logTraffic;
    private bool _isGatewayRunning;
    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private string _provider = string.Empty;
    private string _codexBaseUrl = string.Empty;
    private string _environmentKey = "DZDY_API_KEY";
    private BackupItem? _selectedBackup;
    private string _gatewayStatus = "已停止";
    private string _codexStatus = "就绪";
    private string _applicationStatus = string.Empty;
    private bool _isApiKeyVisible;

    public MainWindowViewModel(
        AppPaths paths,
        GatewaySettingsService gatewaySettingsService,
        GatewayHostService gatewayHost,
        CodexConfigService codexConfig,
        CodexAuthService codexAuth,
        CodexBackupService backupService,
        EnvironmentVariableService environmentService,
        ModelService modelService,
        ApplicationLauncher launcher)
    {
        _paths = paths;
        _gatewaySettingsService = gatewaySettingsService;
        _gatewayHost = gatewayHost;
        _codexConfig = codexConfig;
        _codexAuth = codexAuth;
        _backupService = backupService;
        _environmentService = environmentService;
        _modelService = modelService;
        _launcher = launcher;

        _startGatewayCommand = new AsyncCommand(StartGatewayAsync, () => !IsGatewayRunning);
        _stopGatewayCommand = new AsyncCommand(StopGatewayAsync, () => IsGatewayRunning);
        StartGatewayCommand = _startGatewayCommand;
        StopGatewayCommand = _stopGatewayCommand;
        SaveGatewayCommand = new AsyncCommand(SaveGatewayAsync);
        FetchModelsCommand = new AsyncCommand(FetchModelsAsync);
        SaveCodexCommand = new AsyncCommand(SaveCodexAsync);
        RestoreBackupCommand = new AsyncCommand(RestoreBackupAsync);
        RefreshBackupsCommand = new AsyncCommand(RefreshBackupsAsync);
        LaunchCodexCommand = new AsyncCommand(LaunchCodexAsync);
        LaunchChatGptCommand = new AsyncCommand(LaunchChatGptAsync);
        OpenCodexDirectoryCommand = new AsyncCommand(OpenCodexDirectoryAsync);
        ToggleApiKeyCommand = new AsyncCommand(() =>
        {
            IsApiKeyVisible = !IsApiKeyVisible;
            return Task.CompletedTask;
        });
        ClearLogsCommand = new AsyncCommand(() =>
        {
            GatewayLogs.Clear();
            return Task.CompletedTask;
        });

        _gatewayHost.LogReceived += message => Dispatcher.UIThread.Post(() =>
        {
            GatewayLogs.Add(message);
            while (GatewayLogs.Count > 300)
            {
                GatewayLogs.RemoveAt(0);
            }
        });

        Load();
    }

    public ObservableCollection<string> Models { get; } = [];
    public ObservableCollection<BackupItem> Backups { get; } = [];
    public ObservableCollection<string> GatewayLogs { get; } = [];

    public AsyncCommand StartGatewayCommand { get; }
    public AsyncCommand StopGatewayCommand { get; }
    public AsyncCommand SaveGatewayCommand { get; }
    public AsyncCommand FetchModelsCommand { get; }
    public AsyncCommand SaveCodexCommand { get; }
    public AsyncCommand RestoreBackupCommand { get; }
    public AsyncCommand RefreshBackupsCommand { get; }
    public AsyncCommand LaunchCodexCommand { get; }
    public AsyncCommand LaunchChatGptCommand { get; }
    public AsyncCommand OpenCodexDirectoryCommand { get; }
    public AsyncCommand ToggleApiKeyCommand { get; }
    public AsyncCommand ClearLogsCommand { get; }

    public string LocalBindIp { get => _localBindIp; set => SetProperty(ref _localBindIp, value); }
    public int ListenPort { get => _listenPort; set => SetProperty(ref _listenPort, value); }
    public string UpstreamBaseUrl { get => _upstreamBaseUrl; set => SetProperty(ref _upstreamBaseUrl, value); }
    public string UserAgent { get => _userAgent; set => SetProperty(ref _userAgent, value); }
    public bool OverwriteUserAgent { get => _overwriteUserAgent; set => SetProperty(ref _overwriteUserAgent, value); }
    public bool LogTraffic { get => _logTraffic; set => SetProperty(ref _logTraffic, value); }
    public bool IsGatewayRunning
    {
        get => _isGatewayRunning;
        private set
        {
            if (SetProperty(ref _isGatewayRunning, value))
            {
                _startGatewayCommand.RaiseCanExecuteChanged();
                _stopGatewayCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(GatewayStateText));
            }
        }
    }
    public string GatewayStateText => IsGatewayRunning ? "运行中" : "已停止";
    public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string Provider { get => _provider; set => SetProperty(ref _provider, value); }
    public string CodexBaseUrl { get => _codexBaseUrl; set => SetProperty(ref _codexBaseUrl, value); }
    public string EnvironmentKey { get => _environmentKey; set => SetProperty(ref _environmentKey, value); }
    public BackupItem? SelectedBackup
    {
        get => _selectedBackup;
        set => SetProperty(ref _selectedBackup, value);
    }
    public string GatewayStatus { get => _gatewayStatus; private set => SetProperty(ref _gatewayStatus, value); }
    public string CodexStatus { get => _codexStatus; private set => SetProperty(ref _codexStatus, value); }
    public string ApplicationStatus { get => _applicationStatus; private set => SetProperty(ref _applicationStatus, value); }
    public bool IsApiKeyVisible { get => _isApiKeyVisible; set => SetProperty(ref _isApiKeyVisible, value); }
    public string CodexDirectory => _paths.CodexDirectory;

    private void Load()
    {
        try
        {
            ApplyGateway(_gatewaySettingsService.Load());
            ApplyCodex(_codexConfig.Load());
            ApiKey = _environmentService.Read(EnvironmentKey);
            RefreshBackups();
            RefreshApplicationStatus();
            GatewayStatus = $"配置文件：{_gatewaySettingsService.SettingsPath}";
        }
        catch (Exception exception)
        {
            CodexStatus = exception.Message;
        }
    }

    private async Task SaveGatewayAsync()
    {
        try
        {
            await _gatewaySettingsService.SaveAsync(CurrentGatewaySettings());
            GatewayStatus = "网关配置已保存；运行中的网关需重启后应用。";
        }
        catch (Exception exception)
        {
            GatewayStatus = $"保存失败：{exception.Message}";
        }
    }

    private async Task StartGatewayAsync()
    {
        try
        {
            await _gatewaySettingsService.SaveAsync(CurrentGatewaySettings());
            await _gatewayHost.StartAsync(CurrentGatewaySettings());
            IsGatewayRunning = true;
            GatewayStatus = $"监听 http://{LocalBindIp}:{ListenPort}，上游 {UpstreamBaseUrl}";
        }
        catch (Exception exception)
        {
            GatewayStatus = $"启动失败：{exception.Message}";
        }
    }

    private async Task StopGatewayAsync()
    {
        try
        {
            await _gatewayHost.StopAsync();
            IsGatewayRunning = false;
            GatewayStatus = "网关已停止。";
        }
        catch (Exception exception)
        {
            GatewayStatus = $"停止失败：{exception.Message}";
        }
    }

    private async Task FetchModelsAsync()
    {
        try
        {
            CodexStatus = "正在验证令牌并获取模型…";
            var models = await _modelService.FetchAsync(CodexBaseUrl, ApiKey);
            var previous = Model;
            Models.Clear();
            foreach (var item in models)
            {
                Models.Add(item);
            }
            Model = models.Contains(previous, StringComparer.Ordinal) ? previous : models[0];
            CodexStatus = $"令牌验证通过，已获取 {models.Count} 个模型。";
        }
        catch (Exception exception)
        {
            CodexStatus = $"获取模型失败：{exception.Message}";
        }
    }

    private async Task SaveCodexAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new InvalidOperationException("令牌不能为空。");
            }

            var backup = (File.Exists(_paths.CodexConfigPath) || File.Exists(_paths.CodexAuthPath))
                ? _backupService.Create().DisplayName
                : "首次配置，无旧文件";
            await _environmentService.SaveAsync(EnvironmentKey, ApiKey);
            await _codexConfig.SaveAsync(CurrentCodexSettings());
            var authResult = await _codexAuth.EnsureAsync();
            RefreshBackups();
            CodexStatus = $"配置已保存；自动备份：{backup}{(authResult.PlaceholderCreated ? "；已创建 auth.json 安全占位 Key" : string.Empty)}。";
        }
        catch (Exception exception)
        {
            CodexStatus = $"保存失败：{exception.Message}";
        }
    }

    private Task RefreshBackupsAsync()
    {
        try
        {
            RefreshBackups();
            CodexStatus = $"已刷新，共 {Backups.Count} 个备份。";
        }
        catch (Exception exception)
        {
            CodexStatus = $"刷新失败：{exception.Message}";
        }
        return Task.CompletedTask;
    }

    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is null)
        {
            return;
        }

        try
        {
            if (File.Exists(_paths.CodexConfigPath) || File.Exists(_paths.CodexAuthPath))
            {
                _backupService.Create();
            }
            var selected = SelectedBackup;
            await _backupService.RestoreAsync(selected);
            ApplyCodex(_codexConfig.Load());
            ApiKey = _environmentService.Read(EnvironmentKey);
            RefreshBackups();
            CodexStatus = $"已还原：{selected.DisplayName}";
        }
        catch (Exception exception)
        {
            CodexStatus = $"还原失败：{exception.Message}";
        }
    }

    private Task LaunchCodexAsync()
    {
        try
        {
            _launcher.LaunchCodex(_paths.UserHome);
            CodexStatus = "已发送 Codex 启动请求。";
        }
        catch (Exception exception)
        {
            CodexStatus = $"启动失败：{exception.Message}";
        }
        return Task.CompletedTask;
    }

    private Task LaunchChatGptAsync()
    {
        try
        {
            _launcher.LaunchChatGpt(_paths.UserHome);
            CodexStatus = "已发送 ChatGPT 启动请求。";
        }
        catch (Exception exception)
        {
            CodexStatus = $"启动失败：{exception.Message}";
        }
        return Task.CompletedTask;
    }

    private Task OpenCodexDirectoryAsync()
    {
        try
        {
            _launcher.OpenDirectory(_paths.CodexDirectory);
        }
        catch (Exception exception)
        {
            CodexStatus = $"打开目录失败：{exception.Message}";
        }
        return Task.CompletedTask;
    }

    private void RefreshBackups()
    {
        var previousId = SelectedBackup?.Id;
        Backups.Clear();
        foreach (var item in _backupService.List())
        {
            Backups.Add(item);
        }
        SelectedBackup = Backups.FirstOrDefault(item => item.Id == previousId) ?? Backups.FirstOrDefault();
    }

    private void RefreshApplicationStatus()
    {
        var chatGpt = _launcher.DetectChatGpt();
        var codex = _launcher.DetectCodex();
        ApplicationStatus = $"ChatGPT：{chatGpt.Description}  |  Codex：{codex.Description}";
    }

    private GatewaySettings CurrentGatewaySettings() => new()
    {
        LocalBindIp = LocalBindIp,
        ListenPort = ListenPort,
        UpstreamBaseUrl = UpstreamBaseUrl,
        UserAgent = UserAgent,
        OverwriteUserAgent = OverwriteUserAgent,
        LogTraffic = LogTraffic,
        ExtraRequestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8"
        }
    };

    private CodexSettings CurrentCodexSettings() => new()
    {
        Model = Model,
        Provider = Provider,
        BaseUrl = CodexBaseUrl,
        EnvironmentKey = EnvironmentKey
    };

    private void ApplyGateway(GatewaySettings settings)
    {
        LocalBindIp = settings.LocalBindIp;
        ListenPort = settings.ListenPort;
        UpstreamBaseUrl = settings.UpstreamBaseUrl;
        UserAgent = settings.UserAgent;
        OverwriteUserAgent = settings.OverwriteUserAgent;
        LogTraffic = settings.LogTraffic;
    }

    private void ApplyCodex(CodexSettings settings)
    {
        Model = settings.Model;
        Provider = settings.Provider;
        CodexBaseUrl = settings.BaseUrl;
        EnvironmentKey = settings.EnvironmentKey;
        Models.Clear();
        Models.Add(Model);
    }
}
