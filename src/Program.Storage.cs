#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TabBouncer;

// 설정 파일, 활동 로그, 판정 이벤트, 누적 통계를 담당한다.
internal static partial class Program
{
    private const long MaxLogBytes = 5L * 1024 * 1024;
    private static bool _configDirectoryFallback;
    private static StatsData _stats = new();

    private sealed class StatsData
    {
        public long TotalClosed { get; set; }
        public long TotalReverted { get; set; }
        public DateTimeOffset Since { get; set; } = DateTimeOffset.Now;
    }

    // 실행 파일 폴더에 쓸 수 있으면 그곳(포터블)을, 아니면 %LOCALAPPDATA%\TabBouncer를 설정 폴더로 쓴다.
    // 폴백할 때 실행 파일 옆에 읽기 전용 config.json이 있으면 첫 실행에 한해 씨앗으로 복사한다.
    private static string ResolveConfigDirectory()
    {
        string beside = AppContext.BaseDirectory;
        if (IsWritableDirectory(beside))
        {
            _configDirectoryFallback = false;
            return beside;
        }

        _configDirectoryFallback = true;
        string fallback = _dataDirectory;
        try
        {
            Directory.CreateDirectory(fallback);
            string seed = Path.Combine(beside, "config.json");
            string target = Path.Combine(fallback, "config.json");
            if (File.Exists(seed) && !File.Exists(target))
                File.Copy(seed, target);
        }
        catch
        {
        }
        return fallback;
    }

    private static bool IsWritableDirectory(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, ".tabbouncer-write-test-" + Environment.ProcessId);
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                       FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 감시 켜기/끄기는 GUI에서만 바꾼다. 설정 파일을 다시 읽어도 현재 감시 상태는 유지한다.
    // 관측 모드는 GUI에서 잠시 바꿀 수 있으므로, 파일의 dryRun 값이 지난번과 달라졌거나(파일을 직접 고침)
    // 설정 화면에서 저장했을 때(adoptFileDryRun)만 파일 값을 따른다.
    private static void LoadOrCreateConfig(bool adoptFileDryRun = false)
    {
        bool enabled = _config.Enabled;
        bool runtimeDryRun = _config.DryRun;
        Config loaded;
        try
        {
            // scoop의 persist처럼 빈 파일을 먼저 만들어 두는 경우도 새 파일로 본다.
            if (!File.Exists(ConfigPath) || string.IsNullOrWhiteSpace(File.ReadAllText(ConfigPath, Encoding.UTF8)))
            {
                loaded = Config.Defaults();
                SaveConfigFile(loaded);
            }
            else
            {
                loaded = JsonSerializer.Deserialize<Config>(
                    File.ReadAllText(ConfigPath, Encoding.UTF8), JsonOptions) ?? Config.Defaults();
                loaded.Resolve();
                if (loaded.MigrateBuiltinAdDomains())
                {
                    loaded.Resolve();
                    if (SaveConfigFile(loaded))
                        Info(L.T("log.adDomainsMigrated"));
                }
            }
        }
        catch (Exception ex)
        {
            Error(L.Format("log.configLoadFailed", ex.Message));
            loaded = Config.Defaults();
        }

        loaded.Enabled = enabled;
        bool fileDryRun = loaded.DryRun;
        if (_configLoaded && !adoptFileDryRun && fileDryRun == _lastFileDryRun)
            loaded.DryRun = runtimeDryRun;
        _lastFileDryRun = fileDryRun;
        _configLoaded = true;
        _config = loaded;
        ApplyPersistentArguments(_launchArguments);
        _config.Resolve();
    }

    private static bool _configLoaded;
    private static bool _lastFileDryRun;

    // 명령줄 인수나 GUI에서 잠시 바꾼 값이 섞이지 않도록, 파일에 실제로 저장된 설정만 읽는다.
    private static Config ReadConfigFileOrDefaults()
    {
        try
        {
            if (File.Exists(ConfigPath) &&
                JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath, Encoding.UTF8), JsonOptions)
                    is { } file)
            {
                file.MigrateBuiltinAdDomains();
                file.Resolve();
                return file;
            }
        }
        catch
        {
        }
        return Config.Defaults();
    }

    private static bool SaveConfigFile(Config config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(config, JsonOptions),
                new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            Error(L.Format("log.configSaveFailed", ex.Message));
            return false;
        }
    }

    // 목록 등록처럼 일부만 바꿀 때 쓴다. 파일에 저장된 설정에 변경을 적용해 저장하고,
    // 실행 중인 설정은 복사본에 같은 변경을 적용한 뒤 한 번에 바꿔 끼워 판정 스레드와 충돌하지 않게 한다.
    private static bool UpdateConfigFile(Action<Config> mutate)
    {
        Config file = ReadConfigFileOrDefaults();
        mutate(file);
        file.Resolve();

        Config current = _config;
        Config next = CloneConfig(current);
        mutate(next);
        next.Resolve();
        next.Enabled = _config.Enabled;
        next.DryRun = _config.DryRun;
        _config = next;
        return SaveConfigFile(file);
    }

    private static Config CloneConfig(Config source)
    {
        Config copy = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)
                      ?? Config.Defaults();
        copy.Resolve();
        return copy;
    }

    private static void StartConfigWatcher()
    {
        try
        {
            _configWatcher = new FileSystemWatcher(Path.GetDirectoryName(ConfigPath)!, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                               NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            long lastReload = 0;
            FileSystemEventHandler reload = (_, _) =>
            {
                long now = Now;
                if (now - Interlocked.Read(ref lastReload) < 500)
                    return;
                Interlocked.Exchange(ref lastReload, now);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    LoadOrCreateConfig();
                    PushStateToGuide();
                    Info(L.T("log.configReloaded"));
                });
            };
            _configWatcher.Changed += reload;
            _configWatcher.Created += reload;
            _configWatcher.Renamed += (sender, eventArgs) => reload(sender, eventArgs);
        }
        catch (Exception ex)
        {
            Warning(L.Format("log.configWatchFailed", ex.Message));
        }
    }

    private static void WriteEvent(object payload)
    {
        try
        {
            var record = new JsonObject
            {
                ["ts"] = DateTimeOffset.Now.ToString("O"),
                ["data"] = JsonNode.Parse(JsonSerializer.Serialize(payload, JsonOptions))
            };
            lock (LogLock)
            {
                Directory.CreateDirectory(_dataDirectory);
                RotateIfNeeded(EventLogPath);
                File.AppendAllText(
                    EventLogPath,
                    record.ToJsonString() + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }

    private static void WriteLine(string level, string message)
    {
        DateTime now = DateTime.Now;
        lock (LogLock)
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                RotateIfNeeded(LogFilePath);
                File.AppendAllText(
                    LogFilePath,
                    $"{now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        LogEmitted?.Invoke(new AppLogEntry(now, level, message));
    }

    private static void Info(string message) => WriteLine("info", message);
    private static void Success(string message) => WriteLine("success", message);
    private static void Warning(string message) => WriteLine("warning", message);
    private static void Error(string message) => WriteLine("error", message);

    // 파일이 5MB를 넘으면 file → file.1 → file.2 순서로 밀어내고 가장 오래된 것은 지운다.
    // LogLock 안에서만 호출한다.
    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxLogBytes)
                return;
            string first = path + ".1";
            string second = path + ".2";
            if (File.Exists(second))
                File.Delete(second);
            if (File.Exists(first))
                File.Move(first, second);
            File.Move(path, first);
        }
        catch
        {
        }
    }

    private static void WriteStartupLog()
    {
        Info(L.Format("log.startup", Version, Environment.OSVersion.Version, Environment.Version));
        Info(L.Format("log.configPath", ConfigPath));
        Info(L.Format("log.dataDirectory", _dataDirectory));
        if (_configDirectoryFallback)
            Warning(L.T("log.configFallback"));
        if (L.LoadError.Length > 0)
            Warning(L.Format("log.languageFileFailed", L.LoadError));
    }

    internal static IReadOnlyList<string> ReadLogTail(int lines)
    {
        try
        {
            lock (LogLock)
            {
                using var stream = new FileStream(LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long start = Math.Max(0, stream.Length - 64 * 1024);
                stream.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string[] all = reader.ReadToEnd().Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.Length > 0)
                    .ToArray();
                if (start > 0 && all.Length > 0)
                    all = all[1..];
                return all.Skip(Math.Max(0, all.Length - lines)).ToArray();
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void LoadStats()
    {
        try
        {
            if (File.Exists(StatsPath) &&
                JsonSerializer.Deserialize<StatsData>(File.ReadAllText(StatsPath, Encoding.UTF8), JsonOptions)
                    is { } loaded)
            {
                _stats = loaded;
                return;
            }
        }
        catch
        {
        }

        // 통계 파일이 없던 이전 버전 사용자는 남아 있는 이벤트 기록으로 누적 값을 채운다.
        _stats = new StatsData();
        foreach (JsonNode record in ReadEventRecords(int.MaxValue))
        {
            string stage = record["data"]?["stage"]?.GetValue<string>() ?? "";
            if (stage == "closed")
                _stats.TotalClosed++;
            else if (stage == "redirect-blocked")
                _stats.TotalReverted++;
        }
        SaveStats();
    }

    private static void SaveStats()
    {
        try
        {
            File.WriteAllText(StatsPath, JsonSerializer.Serialize(_stats, JsonOptions), new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static void IncrementStats(ClosedKind kind)
    {
        lock (LogLock)
        {
            if (kind == ClosedKind.ClosedTab)
                _stats.TotalClosed++;
            else if (kind == ClosedKind.RevertedRedirect)
                _stats.TotalReverted++;
            SaveStats();
        }
    }

    // events.jsonl.2 → .1 → 현재 파일 순서로, 각 파일의 끝부분만 읽는다.
    private static IEnumerable<JsonNode> ReadEventRecords(long tailBytesPerFile)
    {
        foreach (string path in new[] { EventLogPath + ".2", EventLogPath + ".1", EventLogPath })
        {
            if (!File.Exists(path))
                continue;

            string[] lines;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long start = tailBytesPerFile >= stream.Length ? 0 : stream.Length - tailBytesPerFile;
                stream.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                lines = reader.ReadToEnd().Split('\n');
                if (start > 0 && lines.Length > 0)
                    lines = lines[1..];
            }
            catch
            {
                continue;
            }

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (node is not null)
                    yield return node;
            }
        }
    }

    // 이전 실행에서 닫거나 되돌리거나 유지한 항목 최근 20개를 목록에 다시 채운다.
    private static void LoadRecentFromEvents()
    {
        try
        {
            var items = new List<ClosedItem>();
            foreach (JsonNode record in ReadEventRecords(256 * 1024))
            {
                JsonNode? data = record["data"];
                string stage = data?["stage"]?.GetValue<string>() ?? "";
                ClosedKind? kind = stage switch
                {
                    "closed" => ClosedKind.ClosedTab,
                    "redirect-blocked" => ClosedKind.RevertedRedirect,
                    "kept" => ClosedKind.Kept,
                    "dry-run" or "redirect-dry-run" => ClosedKind.Observed,
                    _ => null
                };
                if (kind is null || data?["url"]?.GetValue<string>() is not { Length: > 0 } url)
                    continue;

                var breakdown = new List<ScorePart>();
                foreach (JsonNode? part in data["breakdown"]?.AsArray() ?? new JsonArray())
                {
                    if (part?["code"]?.GetValue<string>() is { } code)
                        breakdown.Add(new ScorePart(code, part["points"]?.GetValue<int>() ?? 0));
                }

                DateTime at = DateTimeOffset.TryParse(record["ts"]?.GetValue<string>(), out DateTimeOffset parsed)
                    ? parsed.LocalDateTime
                    : DateTime.MinValue;
                items.Add(new ClosedItem(
                    url,
                    data["openerUrl"]?.GetValue<string>() ?? data["restoredUrl"]?.GetValue<string>() ?? "",
                    data["score"]?.GetValue<int>() ?? 0,
                    data["reason"]?.GetValue<string>() ?? "",
                    at,
                    kind.Value,
                    breakdown,
                    "",
                    Restored: true));
            }

            // 기록 순서대로 묶어, 반복된 항목은 한 행과 횟수로 복원한다.
            var grouped = new List<ClosedItem>();
            foreach (ClosedItem item in items)
                AddOrMerge(grouped, item);

            lock (RecentClosed)
            {
                RecentClosed.Clear();
                RecentClosed.AddRange(grouped.Take(20));
            }
        }
        catch
        {
        }
    }
}
