# LlmGateway

本机 LLM API 反向代理（基于 **YARP**），发布为单文件 `LlmGateway.exe`。

## 解决什么问题

部分模型 API 会校验 / 要求 `User-Agent`。客户端不带 UA 时请求失败。  
本程序在本机监听端口，转发到真实上游，并**自动写入 User-Agent 等请求头**，支持流式响应。

```
AionUi / Snow / 其他客户端
    ->  http://127.0.0.1:3001
    ->  LlmGateway（补 UA + 转发）
    ->  你的模型 API
```

## Avalonia 配置中心

`LlmGateway.Desktop` 提供跨平台桌面界面，并将网关与 Codex BYOK 配置管理整合到同一应用：

- 编辑、保存、启动和停止本地网关，查看脱敏运行日志。
- 管理 `~/.codex/config.toml`，只替换目标模型和 Provider 区块，保留其他配置。
- 保存用户 API Key、验证 `/v1/models`、选择模型，并补全 `auth.json` 安全占位 Key。
- 自动备份 `config.toml`/`auth.json`，支持备份列表和失败回滚还原。
- 深度检测并启动 Windows Store/MSIX、传统注册表、PATH/npm 中的 ChatGPT 和 Codex。
- 快速打开 `.codex` 配置目录。

开发运行桌面界面：`dotnet run --project LlmGateway.Desktop/LlmGateway.Desktop.csproj`。
Windows 会把 Key 写入当前用户环境变量；Linux 会写入 `~/.codex/llm-gateway.env` 并同步到桌面进程环境。Store/MSIX 检测仅在 Windows 上启用，通过开始菜单应用清单和 AppModel 注册表定位 AUMID。

`dev` 分支推送会触发 `.github/workflows/dev-windows.yml`，仅在 Windows runner 发布 `win-x64` 桌面版和网关版产物，不生成 Linux 版本。

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
    "UserAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
    "ExtraRequestHeaders": {
      "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8"
    },
    "OverwriteUserAgent": true,
    "LogTraffic": true
  }
}
```

| 配置项 | 说明 |
|--------|------|
| `Gateway.LocalBindIp` | 本地绑定 IP；允许局域网访问可设为 `0.0.0.0` |
| `Gateway.ListenPort` | 本地监听端口 |
| `Gateway.UpstreamBaseUrl` | 上游模型 API 根地址 |
| `Gateway.UserAgent` | 写入的 UA |
| `Gateway.ExtraRequestHeaders` | 额外请求头 |
| `Gateway.OverwriteUserAgent` | 是否强制覆盖客户端 UA |
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
- 流式函数参数增量，以及多个函数调用交错输出。
- 多模态图片 URL/data URL 输入和 Chat Completions 格式的 Base64 音频输入。
- 结构化输出、推理强度和常用采样参数。

`include` 参数会被接受并忽略，因为 Chat Completions 无法返回 Responses
专有的附加字段。当前兼容层不支持 Responses 独有的 `previous_response_id`、
`conversation`、`background`、内置 Web/File Search、Code Interpreter、MCP、
`input_file`、基于 `file_id` 的图片以及 `store: true`；使用这些能力时会返回
明确的 `400 unsupported_parameter`。其他 API 路径仍由 YARP 原样转发。

## 说明

- 与 New-API / Optaris 同类：本机网关程序；除补头和转发外，提供 Responses 到 Chat Completions 的兼容转换。
- 除 `/v1/responses` 外，路径、Query、Body、流式（SSE）均由 YARP 透传。
- 默认不校验上游 HTTPS 证书宽松模式；生产可按需改 `HttpClient` 配置。
