#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TabBouncer;

// Windows 로그인 시 자동 실행, 바탕화면 바로가기, 탐색기 열기를 담당한다.
internal static class WindowsIntegration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "TabBouncer";

    // dotnet tabbouncer.dll로 실행해도 실제 실행 파일 경로를 쓴다.
    internal static string ExecutablePath
    {
        get
        {
            string? process = Environment.ProcessPath;
            if (process is not null &&
                Path.GetFileName(process).Equals("tabbouncer.exe", StringComparison.OrdinalIgnoreCase))
                return process;
            string beside = Path.Combine(AppContext.BaseDirectory, "tabbouncer.exe");
            return File.Exists(beside) ? beside : process ?? beside;
        }
    }

    private static string AutoRunCommand => $"\"{ExecutablePath}\" --minimized";

    internal static bool IsAutoRunEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    internal static bool SetAutoRun(bool enabled, out string error)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
                key.SetValue(RunValueName, AutoRunCommand, RegistryValueKind.String);
            else if (key.GetValue(RunValueName) is not null)
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // 프로그램 폴더를 옮긴 뒤 실행하면 자동 실행 경로를 새 위치로 고친다.
    internal static void RefreshAutoRunPath()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(RunValueName) is string current &&
                !current.Equals(AutoRunCommand, StringComparison.OrdinalIgnoreCase))
                key.SetValue(RunValueName, AutoRunCommand, RegistryValueKind.String);
        }
        catch
        {
        }
    }

    internal static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    internal static bool CreateDesktopShortcut(string url, out string shortcutPath, out string error)
    {
        shortcutPath = "";
        try
        {
            string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new string(host.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"TabBouncer - {safe}.lnk");

            Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                             ?? throw new InvalidOperationException(L.T("error.shellUnavailable"));
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic link = shell.CreateShortcut(shortcutPath);
                link.TargetPath = ExecutablePath;
                link.Arguments = $"--url={url}";
                link.WorkingDirectory = Path.GetDirectoryName(ExecutablePath);
                link.IconLocation = ExecutablePath + ",0";
                link.Description = "TabBouncer: " + url;
                link.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
