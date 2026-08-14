# LlmGateway

`LlmGateway` 是独立 CLI 网关程序，`LlmGateway.Desktop` 是配置与托盘控制程序。

在 Windows 上将两个发布程序与同一份 `appsettings.json` 放在同一个目录。桌面程序可安装 CLI 为名为 `LlmGateway` 的 Windows 服务；服务使用 `--service` 模式启动，并从 CLI 所在目录读取配置。界面保存网关配置后，会自动重启正在运行的服务，使上游地址、端口和请求头立即生效。

服务安装、启动、停止和卸载需要以管理员身份运行桌面程序。关闭主窗口会最小化到系统托盘；从托盘退出仅关闭桌面控制程序，不会停止已安装的网关服务。

# LlmGateway

本机 LLM API 反向代理（基于 **YARP**），发布为单文件 `LlmGateway.exe`。

## 解决什么问题

本程序在本机监听端口，并通过 `Gateway:ResponsesMode` 映射文字模型：`Auto` 先原样调用上游 `/v1/responses`，仅在上游明确不支持端点时自动转换到 `/v1/chat/completions`；`Responses` 强制原样转发；`ChatCompletions` 强制协议转换。图像、视频和 Gemini 特殊 API 由 YARP 透明转发，并可通过 `Gateway:EndpointMappings` 配置外部路径到供应商路径的前缀映射。

```
Codex / 其他客户端
    ->  http://127.0.0.1:3001/v1
    ->  LlmGateway（Responses 兼容转换 + 转发）
    ->  你的模型 API
```

## Avalonia 配置中心

`LlmGateway.Desktop` 提供跨平台桌面界面，并将网关与 Codex BYOK 配置管理整合到同一应用：

- 在同一配置页编辑并保存网关和 Codex BYOK 参数，启动或停止本地网关并查看脱敏运行日志。
- 兼容模式自动将 Codex Base URL 设置为 `http://127.0.0.1:[监听端口]/v1`；关闭后可配置直连地址。
- 管理 `~/.codex/config.toml`，只替换目标模型和 Provider 区块，保留其他配置。
- 保存用户 API Key、验证 `/v1/models`、选择模型，并补全 `auth.json` 安全占位 Key。
- 自动备份 `config.toml`/`auth.json`，支持备份列表和失败回滚还原。
- 检测并启动 Windows Store/MSIX、传统注册表或 PATH 中的 ChatGPT。
- 快速打开 `.codex` 配置目录。

开发运行桌面界面：`dotnet run --project LlmGateway.Desktop/LlmGateway.Desktop.csproj`。
Windows 会把 Key 写入当前用户环境变量；Linux 会写入 `~/.codex/llm-gateway.env` 并同步到桌面进程环境。Store/MSIX 检测仅在 Windows 上启用，通过开始菜单应用清单和 AppModel 注册表定位 AUMID。

macOS 会将变量写入 `~/.codex/llm-gateway.env`，同时通过 `launchctl setenv` 更新当前登录会话，并在 `~/Library/LaunchAgents/` 创建用户 LaunchAgent，以便下次登录后新启动的 GUI 客户端也能读取。不会修改 `~/.zshrc`。

推送或提交 Pull Request 到 `dev`、`main` 或 `ailili-main` 会触发 [.github/workflows/dev-windows.yml](.github/workflows/dev-windows.yml)。工作流仅使用 Windows runner，运行测试并发布合并后的 `win-x64` 桌面版和网关版产物，不构建 Linux 版本。

## 环境要求

- 开发/发布：**.NET 10 SDK**（https://dotnet.microsoft.com/download/dotnet/10.0）
- 运行发布后的 exe：无需再装 SDK（已 self-contained）

本机若只有 Runtime、没有 SDK，请先安装 SDK 再执行 publish。

## 配置

编辑 `appsettings.json`（发布后与 exe 同目录）：

```json
{
  "Gateway": {
    "LocalBindIp": "127.0.0.1",
    "ListenPort": 3001,
    "UpstreamBaseUrl": "https://你的模型API根地址",
    "CompatibilityMode": true,
    "DirectCodexBaseUrl": "https://你的模型API根地址/v1",
    "ExtraRequestHeaders": {
      "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8"
    },
    "LogTraffic": true
  }
}
```

| 配置项 | 说明 |
|--------|------|
| `Gateway.LocalBindIp` | 本地绑定 IP；允许局域网访问可设为 `0.0.0.0` |
| `Gateway.ListenPort` | 本地监听端口 |
| `Gateway.UpstreamBaseUrl` | 网关转发到的上游模型 API 根地址 |
| `Gateway.CompatibilityMode` | 是否让 Codex 自动连接本地网关 |
| `Gateway.DirectCodexBaseUrl` | 关闭兼容模式时使用的 Codex 直连地址 |
| `Gateway.ExtraRequestHeaders` | 额外请求头；`User-Agent` 不会由配置主动写入 |
| `Gateway.LogTraffic` | 是否记录客户端与上游的请求头、请求体、响应头和响应体 |

改配置后**重启 exe** 生效。流量日志可能包含 API Key 和对话内容，只应在受信任环境开启。

## 开发运行

在项目目录执行 `dotnet run`，程序会按当前操作系统运行。

浏览器打开 http://127.0.0.1:3001/ 可看状态。

## 发布单文件程序

Windows x64：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64
```

Linux x64：

```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/linux-x64
```

发布目录会同时包含可执行程序和 `appsettings.json`。

## 客户端怎么指

假设上游是 OpenAI 兼容接口：

- 原 base_url：`https://api.xxx.com/v1`
- 改成：`http://127.0.0.1:3001/v1`
- API Key 仍用原来的（原样转发 Authorization 等头）

## Responses 兼容转换

客户端请求 `POST /v1/responses` 时，网关会把请求转换后发送到上游的
`POST /v1/chat/completions`，并把上游结果转换成 Responses API 格式返回。

兼容层支持：

- 非流式和流式文本；SSE 输出包含 Responses 生命周期、文本增量及完成事件。
- 自定义函数工具定义、指定工具、并行工具调用、工具结果回传。
- Web Search、File Search、Computer 和 MCP 工具声明降级为 Chat Completions 函数。
- 流式函数参数增量，以及多个函数调用交错输出。
- 多模态图片 URL/data URL 输入和 Chat Completions 格式的 Base64 音频输入。
- 结构化输出、推理强度和常用采样参数。

内置工具的降级函数名分别为 `web_search`、`file_search`、`computer_action` 和
`mcp_call_<server_label>`。对应的专用 `tool_choice` 也会转换为函数选择。上游
产生的调用会作为普通 Responses `function_call` 返回；客户端或外部工具系统执行后，
应通过 `function_call_output` 回传结果。网关不会执行搜索、文件检索、计算机操作或
MCP 调用，也不会生成原生 `web_search_call`、`file_search_call` 等执行事件。

`include` 参数会被接受并忽略，因为 Chat Completions 无法返回 Responses 专有的
附加字段。当前兼容层不支持 Responses 独有的 `previous_response_id`、
`conversation`、`background`、内置工具的实际执行、`input_file`、基于 `file_id`
的图片以及 `store: true`。无法降级的其他工具类型会被忽略，其他 API 路径仍由
YARP 原样转发。

## 说明

- 与 New-API / Optaris 同类：本机网关程序；除补头和转发外，提供 Responses 到 Chat Completions 的兼容转换。
- 除 `/v1/responses` 外，路径、Query、Body、流式（SSE）均由 YARP 透传。
- 默认不校验上游 HTTPS 证书宽松模式；生产可按需改 `HttpClient` 配置。
