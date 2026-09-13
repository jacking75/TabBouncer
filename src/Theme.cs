#nullable enable

using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TabBouncer;

// 창들이 함께 쓰는 색과 버튼 모양이다.
internal static class Theme
{
    internal static readonly Color Background = Color.FromArgb(246, 247, 249);
    internal static readonly Color Surface = Color.White;
    internal static readonly Color Primary = Color.FromArgb(37, 99, 235);
    internal static readonly Color TextPrimary = Color.FromArgb(24, 31, 42);
    internal static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
    internal static readonly Color TextMuted = Color.FromArgb(152, 162, 179);
    internal static readonly Color Success = Color.FromArgb(22, 163, 74);
    internal static readonly Color Warning = Color.FromArgb(217, 119, 6);
    internal static readonly Color Error = Color.FromArgb(220, 38, 38);
    internal static readonly Color Border = Color.FromArgb(208, 213, 221);
    internal static readonly Color OffBannerBackground = Color.FromArgb(254, 243, 199);
    internal static readonly Color OffBannerText = Color.FromArgb(146, 64, 14);
    internal static readonly Color ClosedBannerBackground = Color.FromArgb(255, 237, 213);
    internal static readonly Color ClosedBannerText = Color.FromArgb(154, 52, 18);

    private static Icon? _appIcon;

    // dotnet tabbouncer.dll로 실행해도 아이콘이 보이도록 어셈블리에 넣은 리소스에서 읽는다.
    internal static Icon? AppIcon
    {
        get
        {
            if (_appIcon is not null)
                return _appIcon;
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("TabBouncer.tabbouncer.ico");
                if (stream is not null)
                    _appIcon = new Icon(stream);
            }
            catch
            {
            }
            return _appIcon;
        }
    }

    internal static void StyleButton(Button button, string text, bool primary, int minimumWidth = 96)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(minimumWidth, 34);
        button.Padding = new Padding(10, 0, 10, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Primary : Surface;
        button.ForeColor = primary ? Color.White : TextPrimary;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }
}
