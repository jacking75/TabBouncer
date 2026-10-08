using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using TabBouncer;
using App = TabBouncer.Program;

internal static class ShutdownTests
{
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CheckUiExit(closeChrome: true);
            CheckUiExit(closeChrome: false);
            CheckSendDeadlineAsync().GetAwaiter().GetResult();
            CheckClientShutdownAsync().GetAwaiter().GetResult();
            CheckRealChromeExitAsync(closeChrome: false).GetAwaiter().GetResult();
            CheckRealChromeExitAsync(closeChrome: true).GetAwaiter().GetResult();
            Console.WriteLine("종료 회귀 검사 통과다.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CheckUiExit(bool closeChrome)
    {
        using var peer = new SilentPeer();
        var client = new CdpClient();
        client.ConnectAsync(peer.Url, CancellationToken.None).GetAwaiter().GetResult();
        string data = Path.Combine(Path.GetTempPath(), "TabBouncerShutdown-" + Guid.NewGuid());
        Directory.CreateDirectory(data);
        SetField("_dataDirectory", data);
        SetField("_configDirectory", data);
        var config = Config.Defaults();
        config.CloseChromeOnExit = closeChrome ? "always" : "never";
        config.CloseToTray = false;
        SetField("_config", config);
        SetField("_cdp", client);
        SetField("_connected", true);

        try
        {
            using var form = new MainForm();
            using var heartbeat = new System.Windows.Forms.Timer { Interval = 50 };
            using var exitTimer = new System.Windows.Forms.Timer { Interval = 150 };
            var elapsed = new Stopwatch();
            bool responsive = false;
            Exception? error = null;
            heartbeat.Tick += (_, _) =>
            {
                if (elapsed.IsRunning && elapsed.ElapsedMilliseconds < 1000)
                    responsive = true;
            };
            exitTimer.Tick += (_, _) =>
            {
                exitTimer.Stop();
                elapsed.Start();
                try
                {
                    typeof(MainForm).GetMethod("ExitApplication", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, null);
                }
                catch (Exception ex)
                {
                    error = ex;
                    form.Dispose();
                }
            };
            form.Shown += (_, _) => { heartbeat.Start(); exitTimer.Start(); };
            Application.Run(form);
            elapsed.Stop();
            if (error is not null) throw error;
            if (closeChrome)
                Require(responsive, $"Chrome 무응답 중 UI가 멈췄다 ({elapsed.ElapsedMilliseconds}ms).");
            else
                Require(elapsed.ElapsedMilliseconds < 1000 && !peer.MessageReceived.Task.IsCompleted,
                    "Chrome 유지 선택 시 종료 명령 없이 바로 끝나야 한다.");
            Require(elapsed.Elapsed < TimeSpan.FromSeconds(5), "종료 제한 시간을 초과했다.");
            Require(File.Exists(Path.Combine(data, "ui-state.json")), "종료 시 창 상태를 저장하지 않았다.");
            Console.WriteLine($"통과: Chrome {(closeChrome ? "종료" : "유지")} 선택 시 UI 응답과 종료 ({elapsed.ElapsedMilliseconds}ms)다.");
        }
        finally
        {
            client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            SetField("_cdp", null);
            SetField("_connected", false);
            Directory.Delete(data, true);
        }
    }

    private static async Task CheckSendDeadlineAsync()
    {
        using var peer = new SilentPeer();
        await using var client = new CdpClient();
        await client.ConnectAsync(peer.Url, CancellationToken.None);
        var sendLock = (SemaphoreSlim)typeof(CdpClient)
            .GetField("_sendLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        await sendLock.WaitAsync();
        try
        {
            try
            {
                await client.SendAsync("Queued.command", timeoutMs: 100).WaitAsync(TimeSpan.FromSeconds(1));
                throw new InvalidOperationException("송신 잠금 대기 제한 시간이 적용되지 않았다.");
            }
            catch (TimeoutException ex) when (ex.Message.Contains("Queued.command")) { }
        }
        finally
        {
            sendLock.Release();
        }
        try
        {
            await client.SendAsync("Unanswered.command", timeoutMs: 100).WaitAsync(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("응답 대기 제한 시간이 적용되지 않았다.");
        }
        catch (TimeoutException ex) when (ex.Message.Contains("Unanswered.command")) { }
        Console.WriteLine("통과: 송신 잠금 대기와 응답 대기에 명령 제한 시간을 적용한다.");
    }

    private static async Task CheckClientShutdownAsync()
    {
        using var peer = new SilentPeer();
        var client = new CdpClient();
        int closed = 0;
        client.Closed += _ => Interlocked.Increment(ref closed);
        await client.ConnectAsync(peer.Url, CancellationToken.None);
        Task pending = client.SendAsync("No.reply", timeoutMs: 10000);
        await peer.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var elapsed = Stopwatch.StartNew();
        Task[] disposals = Enumerable.Range(0, 4).Select(_ => client.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposals).WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("응답 없는 명령이 성공으로 끝났다.");
        }
        catch (OperationCanceledException) { }
        Require(closed == 1, "수신 루프가 한 번 종료되어야 한다.");
        try
        {
            await client.SendAsync("After.dispose").WaitAsync(TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("정리한 연결에서 새 명령이 성공했다.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine($"통과: 무응답 연결 정리, 대기 명령 취소, 중복 Dispose ({elapsed.ElapsedMilliseconds}ms)다.");
    }

    private static void SetField(string name, object? value) =>
        typeof(App).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task CheckRealChromeExitAsync(bool closeChrome)
    {
        string data = Path.Combine(Path.GetTempPath(), "TabBouncerRealExit-" + Guid.NewGuid());
        Directory.CreateDirectory(data);
        var config = Config.Defaults();
        config.Language = "ko";
        config.ChromePath = App.BrowserCandidates().First(File.Exists);
        config.ChromeArguments.Add("--headless=new");
        config.ChromeArguments.Add("--disable-gpu");
        config.StartUrl = "about:blank";
        config.CloseToTray = false;
        config.CloseChromeOnExit = closeChrome ? "always" : "never";
        config.NotifyOnBlock = false;
        await File.WriteAllTextAsync(Path.Combine(data, "config.json"), App.SerializeConfig(config));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--data-dir=" + data);
        using Process process = Process.Start(start)!;
        string? endpoint = null;
        bool verified = false;
        try
        {
            var deadline = Stopwatch.StartNew();
            string logPath = Path.Combine(data, "tabbouncer.log");
            while (deadline.Elapsed < TimeSpan.FromSeconds(15))
            {
                try
                {
                    if (File.Exists(logPath) && (await File.ReadAllTextAsync(logPath)).Contains("사용자 클릭 추적을 시작했다"))
                        break;
                }
                catch (IOException) { } // 로그를 쓰는 순간의 파일 공유 충돌은 다음 표본에서 다시 확인한다.
                Require(!process.HasExited, "연결 준비 중 앱이 종료됐다.");
                await Task.Delay(100);
            }
            Require(deadline.Elapsed < TimeSpan.FromSeconds(15), "실제 Chrome 연결 준비 시간을 초과했다.");
            string[] activePort = await File.ReadAllLinesAsync(Path.Combine(data, "ChromeProfile", "DevToolsActivePort"));
            endpoint = $"ws://127.0.0.1:{activePort[0]}{activePort[1]}";
            process.Refresh();
            var elapsed = Stopwatch.StartNew();
            // CloseMainWindow의 WM_CLOSE는 TaskManagerClosing으로 처리된다. 사용자 X와 같은 경로를 쓴다.
            Require(PostMessage(process.MainWindowHandle, 0x0112, new IntPtr(0xF060), IntPtr.Zero),
                "앱 창에 사용자 닫기 요청을 보내지 못했다.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Require(process.ExitCode == 0, "앱이 정상 종료 코드를 반환하지 않았다.");
            Require(File.Exists(Path.Combine(data, "ui-state.json")), "실제 앱이 종료 시 창 상태를 저장하지 않았다.");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            bool chromeAlive = true;
            var chromeExit = Stopwatch.StartNew();
            do
            {
                try
                {
                    await http.GetStringAsync($"http://127.0.0.1:{activePort[0]}/json/version");
                    chromeAlive = true;
                }
                catch (HttpRequestException) { chromeAlive = false; }
                catch (TaskCanceledException) { chromeAlive = false; }
                if (closeChrome && chromeAlive)
                    await Task.Delay(100);
            }
            while (closeChrome && chromeAlive && chromeExit.Elapsed < TimeSpan.FromSeconds(3));
            Require(chromeAlive != closeChrome, "실제 Chrome의 유지·종료 결과가 선택과 다르다.");
            Console.WriteLine($"통과: 실제 Chrome {(closeChrome ? "종료" : "유지")}와 앱 프로세스 종료 ({elapsed.ElapsedMilliseconds}ms)다.");
            verified = true;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
            if (endpoint is null)
            {
                string portFile = Path.Combine(data, "ChromeProfile", "DevToolsActivePort");
                if (File.Exists(portFile))
                {
                    string[] activePort = await File.ReadAllLinesAsync(portFile);
                    if (activePort.Length >= 2)
                        endpoint = $"ws://127.0.0.1:{activePort[0]}{activePort[1]}";
                }
            }
            if (endpoint is not null)
            {
                await using var cleanup = new CdpClient();
                try
                {
                    using var stop = new CancellationTokenSource(2000);
                    await cleanup.ConnectAsync(endpoint, stop.Token);
                    await cleanup.SendAsync("Browser.close", timeoutMs: 1000);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or TimeoutException or InvalidOperationException) { }
            }
            // 실패 근거는 보존한다. 성공 시 Chrome 프로필 잠금 해제를 기다려 임시 폴더를 지운다.
            if (verified)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    try { Directory.Delete(data, true); break; }
                    catch (IOException) when (attempt < 29) { await Task.Delay(100); }
                }
            }
            else
            {
                string log = Path.Combine(data, "tabbouncer.log");
                if (File.Exists(log)) Console.Error.WriteLine(await File.ReadAllTextAsync(log));
                Console.Error.WriteLine("실패 근거를 보존했다: " + data);
            }
        }
    }

    // 명령과 close 프레임에 응답하지 않는 로컬 CDP 상대다. 사용자 Chrome에는 연결하지 않는다.
    private sealed class SilentPeer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        internal TaskCompletionSource MessageReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Url { get; }

        internal SilentPeer()
        {
            int port = FreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            Url = $"ws://127.0.0.1:{port}/";
            _server = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                using WebSocket socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                var buffer = new byte[65536];
                while (!_stop.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, _stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Task.Delay(Timeout.Infinite, _stop.Token);
                        return;
                    }
                    MessageReceived.TrySetResult();
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            catch (WebSocketException) { }
        }

        private static int FreePort()
        {
            for (int port = 25001; port <= 25199; port++)
            {
                try
                {
                    using var probe = new TcpListener(IPAddress.Loopback, port);
                    probe.Start();
                    return port;
                }
                catch (SocketException) { }
            }
            throw new InvalidOperationException("검사에 쓸 포트가 없다.");
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
            _server.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _stop.Dispose();
        }
    }
}
