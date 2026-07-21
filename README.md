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

## 说明

- 与 New-API / Optaris 同类：本机网关程序；本项目更轻，只做「补头 + 转发」。
- 路径、Query、Body、流式（SSE）均由 YARP 透传。
- 默认不校验上游 HTTPS 证书宽松模式；生产可按需改 `HttpClient` 配置。
