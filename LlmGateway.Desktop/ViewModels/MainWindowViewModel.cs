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
    private bool _compatibilityMode;
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
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync);
        FetchModelsCommand = new AsyncCommand(FetchModelsAsync);
        RestoreBackupCommand = new AsyncCommand(RestoreBackupAsync);
        RefreshBackupsCommand = new AsyncCommand(RefreshBackupsAsync);
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
    public AsyncCommand SaveConfigurationCommand { get; }
    public AsyncCommand FetchModelsCommand { get; }
    public AsyncCommand RestoreBackupCommand { get; }
    public AsyncCommand RefreshBackupsCommand { get; }
    public AsyncCommand LaunchChatGptCommand { get; }
    public AsyncCommand OpenCodexDirectoryCommand { get; }
    public AsyncCommand ToggleApiKeyCommand { get; }
    public AsyncCommand ClearLogsCommand { get; }

    public string LocalBindIp { get => _localBindIp; set => SetProperty(ref _localBindIp, value); }
    public int ListenPort
    {
        get => _listenPort;
        set
        {
            if (SetProperty(ref _listenPort, value))
            {
                OnPropertyChanged(nameof(EffectiveCodexBaseUrl));
            }
        }
    }
    public string UpstreamBaseUrl
    {
        get => _upstreamBaseUrl;
        set
        {
            if (SetProperty(ref _upstreamBaseUrl, value) && CompatibilityMode)
            {
                ApplyEndpointIdentity(value);
            }
        }
    }
    public bool CompatibilityMode
    {
        get => _compatibilityMode;
        set
        {
            if (SetProperty(ref _compatibilityMode, value))
            {
                ApplyEndpointIdentity(value ? UpstreamBaseUrl : CodexBaseUrl);
                OnPropertyChanged(nameof(IsDirectCodexMode));
                OnPropertyChanged(nameof(EffectiveCodexBaseUrl));
            }
        }
    }
    public bool IsDirectCodexMode => !CompatibilityMode;
    public string EffectiveCodexBaseUrl => CompatibilityMode
        ? $"http://127.0.0.1:{ListenPort}/v1"
        : CodexBaseUrl;
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
    public string CodexBaseUrl
    {
        get => _codexBaseUrl;
        set
        {
            if (SetProperty(ref _codexBaseUrl, value))
            {
                if (!CompatibilityMode)
                {
                    ApplyEndpointIdentity(value);
                }
                OnPropertyChanged(nameof(EffectiveCodexBaseUrl));
            }
        }
    }
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
            ApplyCodex(_codexConfig.Load());
            ApplyGateway(_gatewaySettingsService.Load());
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

    private async Task SaveConfigurationAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new InvalidOperationException("令牌不能为空。");
            }

            var hasExistingConfiguration = File.Exists(_paths.CodexConfigPath) || File.Exists(_paths.CodexAuthPath);
            var previousConfigurationName = EndpointNormalizer.GetConfigurationName(_codexConfig.Load().BaseUrl);
            var backup = hasExistingConfiguration ? _backupService.Create(previousConfigurationName).DisplayName : string.Empty;
            var endpoint = CompatibilityMode ? UpstreamBaseUrl : CodexBaseUrl;
            Provider = EndpointNormalizer.GetConfigurationName(endpoint);
            EnvironmentKey = EndpointNormalizer.GetEnvironmentKey(endpoint);
            await _gatewaySettingsService.SaveAsync(CurrentGatewaySettings());
            await _environmentService.SaveAsync(EnvironmentKey, ApiKey);
            await _codexConfig.SaveAsync(CurrentCodexSettings());
            var authResult = await _codexAuth.EnsureAsync();
            if (!hasExistingConfiguration)
            {
                backup = _backupService.Create("原始配置").DisplayName;
            }
            RefreshBackups();
            GatewayStatus = "全部配置已保存；运行中的网关需重启后应用。";
            CodexStatus = $"Codex 配置已保存；已保存配置：{backup}{(authResult.PlaceholderCreated ? "；已创建 auth.json 安全占位 Key" : string.Empty)}。";
        }
        catch (Exception exception)
        {
            GatewayStatus = $"保存失败：{exception.Message}";
            CodexStatus = GatewayStatus;
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
            var models = await _modelService.FetchAsync(EffectiveCodexBaseUrl, ApiKey);
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
                _backupService.Create("还原前配置");
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

    private void ApplyEndpointIdentity(string endpoint)
    {
        Provider = EndpointNormalizer.GetConfigurationName(endpoint);
        EnvironmentKey = EndpointNormalizer.GetEnvironmentKey(endpoint);
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
        ApplicationStatus = $"ChatGPT：{chatGpt.Description}";
    }

    private GatewaySettings CurrentGatewaySettings() => new()
    {
        LocalBindIp = LocalBindIp,
        ListenPort = ListenPort,
        UpstreamBaseUrl = EndpointNormalizer.Normalize(UpstreamBaseUrl),
        CompatibilityMode = CompatibilityMode,
        DirectCodexBaseUrl = EndpointNormalizer.Normalize(CodexBaseUrl),
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
        BaseUrl = EndpointNormalizer.Normalize(EffectiveCodexBaseUrl),
        EnvironmentKey = EnvironmentKey
    };

    private void ApplyGateway(GatewaySettings settings)
    {
        LocalBindIp = settings.LocalBindIp;
        ListenPort = settings.ListenPort;
        UpstreamBaseUrl = EndpointNormalizer.Normalize(settings.UpstreamBaseUrl);
        CompatibilityMode = settings.CompatibilityMode;
        if (!string.IsNullOrWhiteSpace(settings.DirectCodexBaseUrl))
        {
            CodexBaseUrl = EndpointNormalizer.Normalize(settings.DirectCodexBaseUrl);
        }
        LogTraffic = settings.LogTraffic;
    }

    private void ApplyCodex(CodexSettings settings)
    {
        Model = settings.Model;
        CodexBaseUrl = EndpointNormalizer.Normalize(settings.BaseUrl);
        Provider = EndpointNormalizer.GetConfigurationName(CodexBaseUrl);
        EnvironmentKey = EndpointNormalizer.GetEnvironmentKey(CodexBaseUrl);
        Models.Clear();
        Models.Add(Model);
    }
}
