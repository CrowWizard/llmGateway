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
    private readonly CodexStateService _codexStateService;
    private readonly EnvironmentVariableService _environmentService;
    private readonly CodexPluginService _codexPluginService;
    private readonly CodexSkillService _codexSkillService;
    private readonly PythonRuntimeService _pythonRuntimeService;
    private readonly ModelService _modelService;
    private readonly ApplicationLauncher _launcher;
    private readonly CodexLocalizationService _localizationService;
    private readonly ChatGptInstallerService _chatGptInstallerService;
    private readonly NodeRuntimeService _nodeRuntimeService;
    private readonly AsyncCommand _startGatewayCommand;
    private readonly AsyncCommand _stopGatewayCommand;

    private string _localBindIp = "127.0.0.1";
    private int _listenPort = 23001;
    private string _upstreamBaseUrl = string.Empty;
    private bool _compatibilityMode;
    private bool _logTraffic;
    private bool _isGatewayRunning;
    private string _apiKey = string.Empty;
    private string _imageApiKey = string.Empty;
    private string _imageGenerationStatus = "未校验";
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
        CodexStateService codexStateService,
        EnvironmentVariableService environmentService,
        CodexPluginService codexPluginService,
        CodexSkillService codexSkillService,
        PythonRuntimeService pythonRuntimeService,
        ModelService modelService,
        ApplicationLauncher launcher,
        CodexLocalizationService localizationService,
        ChatGptInstallerService chatGptInstallerService,
        NodeRuntimeService nodeRuntimeService)
    {
        _paths = paths;
        _gatewaySettingsService = gatewaySettingsService;
        _gatewayHost = gatewayHost;
        _codexConfig = codexConfig;
        _codexAuth = codexAuth;
        _backupService = backupService;
        _codexStateService = codexStateService;
        _environmentService = environmentService;
        _codexPluginService = codexPluginService;
        _codexSkillService = codexSkillService;
        _pythonRuntimeService = pythonRuntimeService;
        _modelService = modelService;
        _launcher = launcher;
        _localizationService = localizationService;
        _chatGptInstallerService = chatGptInstallerService;
        _nodeRuntimeService = nodeRuntimeService;

        _startGatewayCommand = new AsyncCommand(StartGatewayAsync, () => !IsGatewayRunning);
        _stopGatewayCommand = new AsyncCommand(StopGatewayAsync, () => IsGatewayRunning);
        StartGatewayCommand = _startGatewayCommand;
        StopGatewayCommand = _stopGatewayCommand;
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync);
        FetchModelsCommand = new AsyncCommand(FetchModelsAsync);
        ValidateImageGenerationCommand = new AsyncCommand(ValidateImageGenerationAsync);
        RestoreBackupCommand = new AsyncCommand(RestoreBackupAsync);
        RefreshBackupsCommand = new AsyncCommand(RefreshBackupsAsync);
        LaunchChatGptCommand = new AsyncCommand(LaunchChatGptAsync);
        InstallChatGptCommand = new AsyncCommand(InstallChatGptAsync);
        OpenCodexDirectoryCommand = new AsyncCommand(OpenCodexDirectoryAsync);
        InstallEcommerceImageStudioCommand = new AsyncCommand(InstallEcommerceImageStudioAsync);
        InstallImageGenAutoCommand = new AsyncCommand(InstallImageGenAutoAsync);
        EnsurePythonCommand = new AsyncCommand(EnsurePythonAsync);
        EnsureNodeCommand = new AsyncCommand(EnsureNodeAsync);
        EnableChineseLocalizationCommand = new AsyncCommand(EnableChineseLocalizationAsync);
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
    public AsyncCommand ValidateImageGenerationCommand { get; }
    public AsyncCommand RestoreBackupCommand { get; }
    public AsyncCommand RefreshBackupsCommand { get; }
    public AsyncCommand LaunchChatGptCommand { get; }
    public AsyncCommand InstallChatGptCommand { get; }
    public AsyncCommand OpenCodexDirectoryCommand { get; }
    public AsyncCommand InstallEcommerceImageStudioCommand { get; }
    public AsyncCommand InstallImageGenAutoCommand { get; }
    public AsyncCommand EnsurePythonCommand { get; }
    public AsyncCommand EnsureNodeCommand { get; }
    public AsyncCommand EnableChineseLocalizationCommand { get; }
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
    public string ImageApiKey { get => _imageApiKey; set => SetProperty(ref _imageApiKey, value); }
    public string ImageGenerationStatus { get => _imageGenerationStatus; private set => SetProperty(ref _imageGenerationStatus, value); }
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
            ImageApiKey = _environmentService.Read("OPENAI_API_KEY");
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
            await _launcher.CloseManagedClientsAsync();
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new InvalidOperationException("令牌不能为空。");
            }
            if (!CompatibilityMode && string.IsNullOrWhiteSpace(ImageApiKey))
            {
                throw new InvalidOperationException("请填写生图 API Key。");
            }

            var hasExistingConfiguration = File.Exists(_paths.CodexConfigPath) || File.Exists(_paths.CodexAuthPath);
            var previousBaseUrl = _codexConfig.Load().BaseUrl;
            var previousConfigurationName = EndpointNormalizer.GetConfigurationName(
                IsLocalGatewayUrl(previousBaseUrl) ? UpstreamBaseUrl : previousBaseUrl);
            var backup = hasExistingConfiguration ? _backupService.Create(previousConfigurationName).DisplayName : string.Empty;
            var endpoint = CompatibilityMode ? UpstreamBaseUrl : CodexBaseUrl;
            Provider = EndpointNormalizer.GetConfigurationName(endpoint);
            EnvironmentKey = EndpointNormalizer.GetEnvironmentKey(endpoint);
            if (CompatibilityMode)
            {
                await _gatewaySettingsService.SaveAsync(CurrentGatewaySettings());
                await _environmentService.SaveAsync(EnvironmentKey, ApiKey);
            }
            else
            {
                await _environmentService.SaveAsync("OPENAI_BASE_URL", EndpointNormalizer.Normalize(CodexBaseUrl));
                await _environmentService.SaveAsync(EnvironmentKey, ApiKey);
                await _environmentService.SaveAsync("OPENAI_API_KEY", ImageApiKey);
            }
            await _codexConfig.SaveAsync(CurrentCodexSettings());
            await _codexStateService.SynchronizeModelProviderAsync(Provider);
            var authResult = await _codexAuth.EnsureAsync();
            if (!hasExistingConfiguration)
            {
                backup = _backupService.Create("原始配置").DisplayName;
            }
            RefreshBackups();
            GatewayStatus = CompatibilityMode
                ? "全部配置已保存；运行中的网关需重启后应用。"
                : "Codex 配置已保存。";
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
            await _launcher.CloseManagedClientsAsync();
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

    private async Task ValidateImageGenerationAsync()
    {
        try
        {
            ImageGenerationStatus = "正在校验生图服务…";
            var models = await _modelService.FetchAsync(CodexBaseUrl, ImageApiKey);
            var imageModel = models.Contains("gpt-image-2", StringComparer.Ordinal)
                ? "gpt-image-2"
                : models.Contains("gpt-image-1.5", StringComparer.Ordinal)
                    ? "gpt-image-1.5"
                    : null;
            ImageGenerationStatus = imageModel is not null
                ? $"生图可用：已获取 {imageModel}。"
                : "生图不可用：未获取到 gpt-image-2 或 gpt-image-1.5。";
        }
        catch (Exception exception)
        {
            ImageGenerationStatus = $"生图不可用：{exception.Message}";
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
            await _codexStateService.SynchronizeModelProviderAsync(Provider);
            ApiKey = _environmentService.Read(EnvironmentKey);
            ImageApiKey = _environmentService.Read("OPENAI_API_KEY");
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

    private async Task InstallChatGptAsync()
    {
        try
        {
            ApplicationStatus = "正在检查 winget 并安装 ChatGPT…";
            var installResult = await _chatGptInstallerService.InstallAsync();
            var chatGpt = _launcher.DetectChatGpt();
            ApplicationStatus = $"{installResult} 当前检测：{chatGpt.Description}";
        }
        catch (Exception exception)
        {
            ApplicationStatus = $"安装 ChatGPT 失败：{exception.Message}";
        }
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

    private async Task InstallEcommerceImageStudioAsync()
    {
        try
        {
            CodexStatus = "正在安装电商生图插件…";
            var installedDirectory = await _codexPluginService.InstallEcommerceImageStudioAsync();
            CodexStatus = $"电商生图插件已安装到：{installedDirectory}";
        }
        catch (Exception exception)
        {
            CodexStatus = $"安装电商生图插件失败：{exception.Message}";
        }
    }

    private async Task InstallImageGenAutoAsync()
    {
        try
        {
            CodexStatus = "正在安装兼容版生图 Skill…";
            var installedDirectory = await _codexSkillService.InstallImageGenAutoAsync();
            CodexStatus = $"兼容版生图 Skill 已安装到：{installedDirectory}";
        }
        catch (Exception exception)
        {
            CodexStatus = $"安装兼容版生图 Skill 失败：{exception.Message}";
        }
    }

    private async Task EnsurePythonAsync()
    {
        try
        {
            CodexStatus = "正在检测 Python 环境…";
            CodexStatus = await _pythonRuntimeService.EnsurePythonAsync();
        }
        catch (Exception exception)
        {
            CodexStatus = $"Python 处理失败：{exception.Message}";
        }
    }

    private async Task EnsureNodeAsync()
    {
        try
        {
            CodexStatus = "正在检测 Node.js 环境…";
            CodexStatus = await _nodeRuntimeService.EnsureNodeAsync();
        }
        catch (Exception exception)
        {
            CodexStatus = $"Node.js 处理失败：{exception.Message}";
        }
    }

    private async Task EnableChineseLocalizationAsync()
    {
        try
        {
            CodexStatus = "正在定位 Codex 的 app.asar…";
            var appAsarPath = _localizationService.FindAppAsarPath();
            await _launcher.CloseManagedClientsAsync();
            var result = _localizationService.EnableChinese(appAsarPath);
            CodexStatus = $"{result.Message} 路径：{result.AppAsarPath}";
        }
        catch (Exception exception)
        {
            CodexStatus = $"启用界面汉化失败：{exception.Message}";
        }
    }

    private static bool IsLocalGatewayUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) ||
         string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

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
