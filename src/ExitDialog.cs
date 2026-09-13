#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace TabBouncer;

// 완전히 종료할 때 전용 Chrome도 닫을지 묻는다.
internal sealed class ExitDialog : Form
{
    private readonly CheckBox _remember = new();

    internal bool CloseChrome { get; private set; }
    internal bool Remember => _remember.Checked;

    internal ExitDialog()
    {
        Text = L.T("exit.title");
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(20, 18, 20, 16)
        };

        root.Controls.Add(new Label
        {
            Text = L.T("exit.question"),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Font = new Font("Segoe UI Semibold", 10.5F),
            Margin = new Padding(0, 0, 0, 6)
        });
        root.Controls.Add(new Label
        {
            Text = L.T("exit.detail"),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 0, 0, 12)
        });

        _remember.Text = L.T("exit.remember");
        _remember.AutoSize = true;
        _remember.Margin = new Padding(0, 0, 0, 14);
        root.Controls.Add(_remember);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };

        var closeChrome = new Button();
        Theme.StyleButton(closeChrome, L.T("exit.closeChrome"), true);
        closeChrome.Click += (_, _) => Finish(true);
        var keepChrome = new Button();
        Theme.StyleButton(keepChrome, L.T("exit.keepChrome"), false);
        keepChrome.Click += (_, _) => Finish(false);
        var cancel = new Button();
        Theme.StyleButton(cancel, L.T("common.cancel"), false);
        cancel.DialogResult = DialogResult.Cancel;

        foreach (Button button in new[] { closeChrome, keepChrome, cancel })
        {
            button.Margin = new Padding(0, 0, 8, 0);
            buttons.Controls.Add(button);
        }
        root.Controls.Add(buttons);

        Controls.Add(root);
        AcceptButton = closeChrome;
        CancelButton = cancel;
    }

    private void Finish(bool closeChrome)
    {
        CloseChrome = closeChrome;
        DialogResult = DialogResult.OK;
        Close();
    }
}
