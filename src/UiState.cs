#nullable enable

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace TabBouncer;

// 창 위치·크기와 화면 표시 선택처럼 설정 파일에 넣을 필요가 없는 사용자 화면 상태다.
internal sealed class UiState
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public bool ShowKept { get; set; } = true;
    public bool TrayHintShown { get; set; }

    internal static UiState Load()
    {
        try
        {
            if (File.Exists(Program.UiStatePath) &&
                JsonSerializer.Deserialize<UiState>(File.ReadAllText(Program.UiStatePath, Encoding.UTF8), Options)
                    is { } state)
                return state;
        }
        catch
        {
        }
        return new UiState();
    }

    internal void Save()
    {
        try
        {
            File.WriteAllText(Program.UiStatePath, JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    // 저장된 위치가 지금 연결된 모니터 안에 충분히 보일 때만 복원한다.
    internal bool TryGetBounds(Size minimum, out Rectangle bounds)
    {
        bounds = new Rectangle(X, Y, Math.Max(Width, minimum.Width), Math.Max(Height, minimum.Height));
        if (Width <= 0 || Height <= 0)
            return false;
        Rectangle candidate = bounds;
        return Screen.AllScreens.Any(screen =>
        {
            Rectangle visible = Rectangle.Intersect(screen.WorkingArea, candidate);
            return visible.Width >= 200 && visible.Height >= 120;
        });
    }
}
