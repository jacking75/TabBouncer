#nullable enable

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabBouncer;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(246, 247, 249);
    private static readonly Color Surface = Color.White;
    private static readonly Color Primary = Color.FromArgb(37, 99, 235);
    private static readonly Color TextPrimary = Color.FromArgb(24, 31, 42);
    private static readonly Color TextSecondary = Color.FromArgb(102, 112, 133);
    private static readonly Color Success = Color.FromArgb(22, 163, 74);
    private static readonly Color Warning = Color.FromArgb(217, 119, 6);
    private static readonly Color BannerBackground = Color.FromArgb(254, 243, 199);
    private static readonly Color BannerText = Color.FromArgb(146, 64, 14);
    private const int BannerHeight = 48;

    private readonly Label _monitoringBanner = new();
    private readonly RowStyle _bannerRow = new(SizeType.Absolute, BannerHeight);
    private readonly Label _connectionDot = new();
    private readonly Label _connectionText = new();
    private readonly Label _monitoringValue = new();
    private readonly Label _modeValue = new();
    private readonly Label _blockedValue = new();
    private readonly Label _scopeValue = new();
    private readonly Button _monitoringButton = new();
    private readonly Button _openChromeButton = new();
    private readonly CheckBox _dryRunCheck = new();
    private readonly ListView _recentList = new();
    private readonly Button _undoButton = new();
    private readonly Button _allowButton = new();
    private readonly RichTextBox _activity = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 750 };
    private string _recentSignature = "";

    internal MainForm()
    {
        Text = "TabBouncer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 620);
        Size = new Size(920, 720);
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(BuildLayout());

        Program.LogEmitted += OnLogEmitted;
        _refreshTimer.Tick += (_, _) => RefreshView();
        _refreshTimer.Start();
        Shown += (_, _) => RefreshView();
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            Program.LogEmitted -= OnLogEmitted;
        };
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 20),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(_bannerRow);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMonitoringBanner(), 0, 1);
        root.Controls.Add(BuildSummary(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        root.Controls.Add(BuildRecentSection(), 0, 4);
        root.Controls.Add(BuildActivitySection(), 0, 5);
        return root;
    }

    private Control BuildMonitoringBanner()
    {
        _monitoringBanner.Dock = DockStyle.Fill;
        _monitoringBanner.Margin = new Padding(0, 0, 0, 6);
        _monitoringBanner.Padding = new Padding(14, 0, 14, 0);
        _monitoringBanner.BackColor = BannerBackground;
        _monitoringBanner.ForeColor = BannerText;
        _monitoringBanner.Font = new Font("Segoe UI Semibold", 11F);
        _monitoringBanner.TextAlign = ContentAlignment.MiddleLeft;
        _monitoringBanner.Text = "감시가 꺼져 있다. \"감시 시작\"을 눌러야 광고 탭·창을 막는다.";
        return _monitoringBanner;
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "TabBouncer",
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = "원치 않는 광고 탭을 조용히 막는다",
            ForeColor = TextSecondary,
            AutoSize = true,
            Location = new Point(3, 39)
        });

        _connectionDot.Text = "●";
        _connectionDot.Font = new Font("Segoe UI", 10F);
        _connectionDot.AutoSize = true;
        _connectionDot.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connectionDot.Location = new Point(panel.Width - 160, 13);
        panel.Controls.Add(_connectionDot);

        _connectionText.AutoSize = true;
        _connectionText.ForeColor = TextSecondary;
        _connectionText.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connectionText.Location = new Point(panel.Width - 139, 14);
        panel.Controls.Add(_connectionText);
        panel.Resize += (_, _) =>
        {
            _connectionText.Left = panel.ClientSize.Width - _connectionText.Width;
            _connectionDot.Left = _connectionText.Left - 21;
        };
        return panel;
    }

    private Control BuildSummary()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(0, 7, 0, 7)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
        row.Controls.Add(CreateSummaryCard("감시 상태", _monitoringValue, 0, 10), 0, 0);
        row.Controls.Add(CreateSummaryCard("작동 모드", _modeValue, 10, 10), 1, 0);
        row.Controls.Add(CreateSummaryCard("최근 차단", _blockedValue, 10, 0), 2, 0);
        return row;
    }

    private static Control CreateSummaryCard(string title, Label value, int left, int right)
    {
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(left, 0, right, 0) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(17, 13, 17, 10) };
        card.Controls.Add(new Label { Text = title, ForeColor = TextSecondary, Dock = DockStyle.Top, Height = 25 });
        value.Font = new Font("Segoe UI Semibold", 13F);
        value.ForeColor = TextPrimary;
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        card.Controls.Add(value);
        outer.Controls.Add(card);
        return outer;
    }

    private Control BuildActions()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 7) };
        StyleButton(_monitoringButton, "감시 시작", true);
        _monitoringButton.Location = new Point(0, 7);
        _monitoringButton.Click += (_, _) => { Program.ToggleMonitoring(); RefreshView(); };
        panel.Controls.Add(_monitoringButton);

        _dryRunCheck.Text = "관측 모드 (탭을 닫지 않음)";
        _dryRunCheck.AutoSize = true;
        _dryRunCheck.Location = new Point(150, 17);
        _dryRunCheck.CheckedChanged += DryRunChanged;
        panel.Controls.Add(_dryRunCheck);

        StyleButton(_openChromeButton, "Chrome 열기", false);
        _openChromeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _openChromeButton.Enabled = false;
        _openChromeButton.Click += (_, _) => { Program.RequestChromeLaunch(); RefreshView(); };
        panel.Controls.Add(_openChromeButton);

        var openConfig = new Button();
        StyleButton(openConfig, "설정 열기", false);
        openConfig.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openConfig.Click += (_, _) => ShowConfigDialog();
        panel.Controls.Add(openConfig);

        panel.Resize += (_, _) =>
        {
            openConfig.Left = panel.ClientSize.Width - openConfig.Width;
            _openChromeButton.Left = openConfig.Left - _openChromeButton.Width - 8;
        };
        return panel;
    }

    private Control BuildRecentSection()
    {
        var group = CreateSection("최근 차단한 탭");
        _recentList.Dock = DockStyle.Fill;
        _recentList.BorderStyle = BorderStyle.None;
        _recentList.View = View.Details;
        _recentList.FullRowSelect = true;
        _recentList.HideSelection = false;
        _recentList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _recentList.Columns.Add("시간", 72);
        _recentList.Columns.Add("점수", 58);
        _recentList.Columns.Add("주소", 430);
        _recentList.Columns.Add("판정", 180);
        _recentList.SelectedIndexChanged += (_, _) => UpdateActionAvailability();
        group.Content.Controls.Add(_recentList);

        StyleButton(_allowButton, "정상 사이트로 등록", false);
        StyleButton(_undoButton, "다시 열기", false);
        _allowButton.Click += (_, _) => { Program.AllowLatestSite(); RefreshView(true); };
        _undoButton.Click += (_, _) => { Program.UndoLatest(); RefreshView(true); };
        group.Actions.Controls.Add(_allowButton);
        group.Actions.Controls.Add(_undoButton);
        return group.Root;
    }

    private Control BuildActivitySection()
    {
        var group = CreateSection("활동");
        _activity.Dock = DockStyle.Fill;
        _activity.BorderStyle = BorderStyle.None;
        _activity.BackColor = Surface;
        _activity.ForeColor = TextSecondary;
        _activity.ReadOnly = true;
        _activity.DetectUrls = false;
        group.Content.Controls.Add(_activity);

        _scopeValue.AutoEllipsis = true;
        _scopeValue.ForeColor = TextSecondary;
        _scopeValue.Dock = DockStyle.Fill;
        _scopeValue.TextAlign = ContentAlignment.MiddleLeft;
        group.Actions.Controls.Add(_scopeValue);
        return group.Root;
    }

    private static (Panel Root, Panel Content, FlowLayoutPanel Actions) CreateSection(string title)
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(16, 10, 16, 12), Margin = new Padding(0, 6, 0, 6) };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = TextPrimary
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };
        var content = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(content);
        root.Controls.Add(actions);
        root.Controls.Add(titleLabel);
        return (root, content, actions);
    }

    private static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.Size = new Size(primary ? 136 : 126, 34);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(208, 213, 221);
        button.BackColor = primary ? Primary : Surface;
        button.ForeColor = primary ? Color.White : TextPrimary;
        button.Cursor = Cursors.Hand;
    }

    private void RefreshView(bool forceRecent = false)
    {
        AppSnapshot snapshot = Program.GetSnapshot();
        _connectionDot.ForeColor = snapshot.Connected ? Success : Warning;
        _connectionText.Text = snapshot.ConnectionStatus;
        _monitoringValue.Text = snapshot.Enabled ? "감시 중" : "일시중지";
        _monitoringValue.ForeColor = snapshot.Enabled ? Success : Warning;
        _modeValue.Text = snapshot.DryRun ? "관측만" : "자동 차단";
        _blockedValue.Text = $"{snapshot.ClosedCount}개";
        _scopeValue.Text = $"감시 범위: {snapshot.WatchedSites}  ·  차단 기준: {snapshot.CloseThreshold}점";
        _monitoringButton.Text = snapshot.Enabled ? "감시 일시중지" : "감시 시작";
        _openChromeButton.Enabled = snapshot.CanOpenChrome;

        bool showBanner = !snapshot.Enabled;
        if (_monitoringBanner.Visible != showBanner)
        {
            _monitoringBanner.Visible = showBanner;
            _bannerRow.Height = showBanner ? BannerHeight : 0;
        }
        string title = snapshot.Enabled ? "TabBouncer" : "TabBouncer - 감시 꺼짐";
        if (Text != title)
            Text = title;

        if (_dryRunCheck.Checked != snapshot.DryRun)
        {
            _dryRunCheck.CheckedChanged -= DryRunChanged;
            _dryRunCheck.Checked = snapshot.DryRun;
            _dryRunCheck.CheckedChanged += DryRunChanged;
        }

        ClosedItem[] items = Program.GetRecentClosed().ToArray();
        string signature = string.Join("|", items.Select(item => $"{item.At.Ticks}:{item.Url}"));
        if (forceRecent || signature != _recentSignature)
        {
            _recentSignature = signature;
            _recentList.BeginUpdate();
            _recentList.Items.Clear();
            foreach (ClosedItem item in items)
            {
                var row = new ListViewItem(item.At.ToString("HH:mm:ss"));
                row.SubItems.Add(item.Score.ToString());
                row.SubItems.Add(item.Url);
                row.SubItems.Add(item.Reason);
                row.Tag = item;
                _recentList.Items.Add(row);
            }
            _recentList.EndUpdate();
        }
        UpdateActionAvailability();
    }

    private void DryRunChanged(object? sender, EventArgs e) => Program.SetDryRun(_dryRunCheck.Checked);

    private void UpdateActionAvailability()
    {
        bool any = _recentList.Items.Count > 0;
        _undoButton.Enabled = any;
        _allowButton.Enabled = any;
    }

    private void OnLogEmitted(AppLogEntry entry)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => OnLogEmitted(entry))); }
            catch (InvalidOperationException) { }
            return;
        }

        Color color = entry.Level switch
        {
            "success" => Success,
            "warning" => Warning,
            "error" => Color.FromArgb(220, 38, 38),
            _ => TextSecondary
        };
        _activity.SelectionStart = _activity.TextLength;
        _activity.SelectionColor = color;
        _activity.AppendText($"[{entry.At:HH:mm:ss}] {entry.Message}{Environment.NewLine}");
        while (_activity.Lines.Length > 120)
        {
            int lineEnd = _activity.Text.IndexOf('\n');
            if (lineEnd < 0) break;
            _activity.Select(0, lineEnd + 1);
            _activity.SelectedText = "";
        }
        _activity.SelectionStart = _activity.TextLength;
        _activity.ScrollToCaret();
    }

    private void ShowConfigDialog()
    {
        using var dialog = new ConfigForm();
        dialog.ShowDialog(this);
        RefreshView(true);
    }
}
