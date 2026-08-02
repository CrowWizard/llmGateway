namespace LlmGateway.Desktop.Services;

public sealed class AppPaths
{
    public AppPaths(string? userHome = null, string? applicationDirectory = null)
    {
        UserHome = userHome ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(UserHome))
        {
            throw new InvalidOperationException("无法确定用户主目录。");
        }

        ApplicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
    }

    public string UserHome { get; }
    public string ApplicationDirectory { get; }
    public string CodexDirectory => Path.Combine(UserHome, ".codex");
    public string CodexConfigPath => Path.Combine(CodexDirectory, "config.toml");
    public string CodexAuthPath => Path.Combine(CodexDirectory, "auth.json");
    public string CodexStateDatabasePath => Path.Combine(CodexDirectory, "state_5.sqlite");
    public string BackupDirectory => Path.Combine(CodexDirectory, "bak");
    public string StateBackupDirectory => Path.Combine(BackupDirectory, "state");
    public string CodexSkillsDirectory => Path.Combine(CodexDirectory, "skills");
    public string ImagePromptGuideDirectory => Path.Combine(ApplicationDirectory, "image-prompt-guide");
    public string GatewaySettingsPath => Path.Combine(ApplicationDirectory, "appsettings.json");
    public string LinuxEnvironmentPath => Path.Combine(CodexDirectory, "llm-gateway.env");
}
