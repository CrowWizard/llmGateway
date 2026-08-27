using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LlmGateway.Desktop.Services;

public sealed class CodexRuntimeLocalizationService(ApplicationLauncher launcher)
{
    public async Task<string> LaunchChineseAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("运行时汉化启动器目前只支持 Windows Codex Desktop。");
        }

        var rendererPort = ReservePort();
        var inspectorPort = ReservePort(rendererPort);
        var arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={rendererPort} --inspect=127.0.0.1:{inspectorPort} --lang=zh-CN";
        var processId = launcher.LaunchChatGpt(AppContext.BaseDirectory, arguments);

        var renderer = await WaitForTargetAsync(rendererPort, target => target.Type is "page" or "iframe" or "webview", cancellationToken);
        var localeResult = await EvaluateAsync(renderer.WebSocketDebuggerUrl, LocaleScript, cancellationToken);
        var inspector = await WaitForTargetAsync(inspectorPort, target => target.Type == "node", cancellationToken);
        var menuResult = await EvaluateAsync(inspector.WebSocketDebuggerUrl, MenuScript, cancellationToken);
        return $"运行时汉化已启动（PID {processId}）。界面：{localeResult}；菜单：{menuResult}";
    }

    private static readonly string LocaleScript = """
        (async function () {
          var started = Date.now();
          while ((!window.electronBridge || typeof window.electronBridge.sendMessageFromView !== 'function') && Date.now() - started < 8000) await new Promise(function (r) { setTimeout(r, 100); });
          var bridge = window.electronBridge;
          if (!bridge) return 'bridge-unavailable';
          var requestId = 'llm-gateway-' + Date.now();
          var response = await new Promise(function (resolve) {
            var timer = setTimeout(function () { resolve({ responseType: 'timeout' }); }, 8000);
            function onMessage(event) { var message = event.data; if (message && message.type === 'fetch-response' && message.requestId === requestId) { clearTimeout(timer); removeEventListener('message', onMessage); resolve(message); } }
            addEventListener('message', onMessage);
            Promise.resolve(bridge.sendMessageFromView({ type: 'fetch', requestId: requestId, method: 'POST', url: 'vscode://codex/set-setting', body: JSON.stringify({ key: 'localeOverride', value: 'zh-CN' }) })).catch(function (error) { resolve({ responseType: 'error', error: String(error) }); });
          });
          if (response.responseType === 'success') { setTimeout(function () { location.reload(); }, 600); return 'ok'; }
          return 'partial:' + JSON.stringify(response);
        })()
        """;

    private static readonly string MenuScript = """
        (function () {
          var electron = typeof process !== 'undefined' && process.getBuiltinModule ? process.getBuiltinModule('electron') : null;
          if (!electron || !electron.Menu) return 'electron-menu-unavailable';
          var translations = { File: '文件', Edit: '编辑', View: '视图', Window: '窗口', Help: '帮助', Undo: '撤销', Redo: '重做', Cut: '剪切', Copy: '复制', Paste: '粘贴', Delete: '删除', 'Select All': '全选', 'New Chat': '新建对话', Settings: '设置', 'Reload Window': '重新加载窗口', 'Toggle Developer Tools': '切换开发者工具', Minimize: '最小化', Exit: '退出', Close: '关闭' };
          function translate(item) { if (!item) return; if (translations[item.label]) item.label = translations[item.label]; if (item.submenu && item.submenu.items) item.submenu.items.forEach(translate); }
          var original = electron.Menu.setApplicationMenu.bind(electron.Menu);
          if (!globalThis.__llmGatewayMenuPatched) { electron.Menu.setApplicationMenu = function (menu) { if (menu && menu.items) menu.items.forEach(translate); return original(menu); }; globalThis.__llmGatewayMenuPatched = true; }
          var current = electron.Menu.getApplicationMenu(); if (current) { current.items.forEach(translate); electron.Menu.setApplicationMenu(current); return 'ok'; } return 'partial:menu-empty';
        })()
        """;

    private static int ReservePort(int except = 0)
    {
        TcpListener listener;
        do
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (port != except) return port;
        } while (true);
    }

    private static async Task<DevToolsTarget> WaitForTargetAsync(int port, Func<DevToolsTarget, bool> predicate, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var json = await client.GetStringAsync($"http://127.0.0.1:{port}/json/list", cancellationToken);
                foreach (var target in JsonSerializer.Deserialize<List<DevToolsTarget>>(json) ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) && predicate(target)) return target;
                }
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(300, cancellationToken);
        }

        throw new TimeoutException($"等待 Codex 运行时调试接口超时（127.0.0.1:{port}）。");
    }

    private static async Task<string> EvaluateAsync(string webSocketUrl, string expression, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);
        var request = JsonSerializer.Serialize(new { id = 1, method = "Runtime.evaluate", @params = new { expression, awaitPromise = true, returnByValue = true, userGesture = true } });
        await socket.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, true, cancellationToken);
        var buffer = new byte[64 * 1024];
        using var output = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        using var document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString() ?? "unknown";
    }

    private sealed class DevToolsTarget
    {
        public string Type { get; set; } = string.Empty;
        public string WebSocketDebuggerUrl { get; set; } = string.Empty;
    }
}