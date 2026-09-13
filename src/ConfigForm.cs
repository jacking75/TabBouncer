#nullable enable

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TabBouncer;

internal sealed class ConfigForm : Form
{
    private static readonly Color Background = Color.FromArgb(246, 247, 249);
    private static readonly Color Surface = Color.White;
    private static readonly Color Primary = Color.FromArgb(37, 99, 235);
    private static readonly Color TextPrimary = Color.FromArgb(24, 31, 42);
    private static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
    private static readonly Color ErrorColor = Color.FromArgb(220, 38, 38);

    private readonly TextBox _editor = new();
    private readonly Label _statusLabel = new();

    internal ConfigForm()
    {
        Text = "설정 (config.json)";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(640, 520);
        Size = new Size(760, 640);
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var pathLabel = new Label
        {
            Text = Program.GetConfigPath(),
            ForeColor = TextSecondary,
            AutoEllipsis = true,
            Dock = DockStyle.Fill
        };
        root.Controls.Add(pathLabel, 0, 0);

        _editor.Multiline = true;
        _editor.ScrollBars = ScrollBars.Both;
        _editor.WordWrap = false;
        _editor.AcceptsTab = true;
        _editor.BackColor = Surface;
        _editor.Font = new Font("Consolas", 10F);
        _editor.Dock = DockStyle.Fill;
        _editor.Text = Program.ReadConfigText().Replace("\n", Environment.NewLine);
        root.Controls.Add(_editor, 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = ErrorColor;
        _statusLabel.AutoEllipsis = true;
        root.Controls.Add(_statusLabel, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var save = new Button();
        StyleButton(save, "저장", true);
        save.Click += (_, _) => Save();
        buttons.Controls.Add(save);

        var cancel = new Button();
        StyleButton(cancel, "취소", false);
        cancel.Click += (_, _) => Close();
        buttons.Controls.Add(cancel);

        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
    }

    private void Save()
    {
        if (Program.TrySaveConfigText(_editor.Text, out string error))
        {
            Close();
            return;
        }

        _statusLabel.Text = "저장하지 못했다: " + error;
    }

    private static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.Size = new Size(96, 34);
        button.Margin = new Padding(8, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(208, 213, 221);
        button.BackColor = primary ? Primary : Surface;
        button.ForeColor = primary ? Color.White : TextPrimary;
        button.Cursor = Cursors.Hand;
    }
}
