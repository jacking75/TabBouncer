#nullable enable

using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabBouncer;

internal sealed class MainForm : Form
{
    private const int BannerHeight = 48;

    private readonly UiState _uiState = UiState.Load();
    private readonly Label _monitoringBanner = new();
    private readonly RowStyle _bannerRow = new(SizeType.Absolute, BannerHeight);
    private readonly Label _connectionDot = new();
    private readonly Label _connectionText = new();
    private readonly Label _monitoringValue = new();
    private readonly Label _modeValue = new();
    private readonly Label _blockedValue = new();
    private readonly Label _blockedDetail = new();
    private readonly Label _scopeValue = new();
    private readonly Button _monitoringButton = new();
    private readonly Button _openChromeButton = new();
    private readonly CheckBox _dryRunCheck = new();
    private readonly TextBox _urlBox = new();
    private readonly ListView _recentList = new();
    private readonly CheckBox _showKeptCheck = new();
    private readonly Button _undoButton = new();
    private readonly Button _allowButton = new();
    private readonly Button _adDomainButton = new();
    private readonly Button _watchButton = new();
    private readonly Label _detailKind = new();
    private readonly TextBox _detailUrl = new();
    private readonly TextBox _detailOpener = new();
    private Label? _detailOpenerCaption;
    private readonly Label _detailScore = new();
    private readonly RichTextBox _activity = new();
    private readonly NotifyIcon _tray = new();
    private readonly ToolTip _toolTip = new();
    private readonly ToolStripMenuItem _trayMonitoring = new();
    private readonly ToolStripMenuItem _trayDryRun = new();
    private readonly ToolStripMenuItem _trayOpenChrome = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 750 };
    private readonly System.Windows.Forms.Timer _notifyTimer = new() { Interval = 3000 };
    private string _recentSignature = "";
    private int _pendingNotifications;
    private string _lastBlockedHost = "";
    private bool _initialVisibilityApplied;
    private bool _exiting;
    private FormWindowState _lastVisibleState = FormWindowState.Normal;

    internal MainForm()
    {
        Text = "TabBouncer";
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 780);
        Size = new Size(1000, 880);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        if (_uiState.TryGetBounds(MinimumSize, out Rectangle bounds))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            if (_uiState.Maximized)
            {
                WindowState = FormWindowState.Maximized;
                _lastVisibleState = FormWindowState.Maximized;
            }
        }

        Controls.Add(BuildLayout());
        BuildTray();

        Program.LogEmitted += OnLogEmitted;
        Program.ItemRecorded += OnItemRecorded;
        Program.ShowRequested += OnShowRequested;
        _refreshTimer.Tick += (_, _) => RefreshView();
        _refreshTimer.Start();
        _notifyTimer.Tick += (_, _) =>
        {
            if (_pendingNotifications > 0)
                FlushNotification();
            else
                _notifyTimer.Stop();
        };
        Shown += (_, _) => RefreshView(true);
        Resize += OnResized;
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            _refreshTimer.Stop();
            _notifyTimer.Stop();
            Program.LogEmitted -= OnLogEmitted;
            Program.ItemRecorded -= OnItemRecorded;
            Program.ShowRequested -= OnShowRequested;
            _tray.Visible = false;
            _tray.Dispose();
            _toolTip.Dispose();
        };
    }

    // --minimized로 실행하면 창을 한 번도 보여 주지 않고 트레이에서 시작한다.
    protected override void SetVisibleCore(bool value)
    {
        if (!_initialVisibilityApplied)
        {
            _initialVisibilityApplied = true;
            if (Program.StartMinimized)
            {
                if (!IsHandleCreated)
                    CreateHandle();
                value = false;
            }
        }
        base.SetVisibleCore(value);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 18),
            ColumnCount = 1,
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(_bannerRow);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMonitoringBanner(), 0, 1);
        root.Controls.Add(BuildSummary(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        root.Controls.Add(BuildQuickOpen(), 0, 4);
        root.Controls.Add(BuildRecentSection(), 0, 5);
        root.Controls.Add(BuildActivitySection(), 0, 6);
        return root;
    }

    private Control BuildMonitoringBanner()
    {
        _monitoringBanner.Dock = DockStyle.Fill;
        _monitoringBanner.Margin = new Padding(0, 0, 0, 6);
        _monitoringBanner.Padding = new Padding(14, 0, 14, 0);
        _monitoringBanner.BackColor = Theme.OffBannerBackground;
        _monitoringBanner.ForeColor = Theme.OffBannerText;
        _monitoringBanner.Font = new Font("Segoe UI Semibold", 11F);
        _monitoringBanner.TextAlign = ContentAlignment.MiddleLeft;
        _monitoringBanner.Text = L.T("main.banner.off");
        return _monitoringBanner;
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            Text = "TabBouncer",
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = $"v{Program.Version}  ·  {L.T("main.subtitle")}",
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Location = new Point(3, 39)
        });

        _connectionDot.Text = "●";
        _connectionDot.Font = new Font("Segoe UI", 10F);
        _connectionDot.AutoSize = true;
        _connectionDot.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(_connectionDot);

        _connectionText.AutoSize = true;
        _connectionText.ForeColor = Theme.TextSecondary;
        _connectionText.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(_connectionText);

        void Place()
        {
            _connectionText.Location = new Point(panel.ClientSize.Width - _connectionText.Width, 14);
            _connectionDot.Location = new Point(_connectionText.Left - 21, 13);
        }
        panel.Resize += (_, _) => Place();
        _connectionText.SizeChanged += (_, _) => Place();
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
        row.Controls.Add(CreateSummaryCard(L.T("main.card.monitoring"), _monitoringValue, null, 0, 10), 0, 0);
        row.Controls.Add(CreateSummaryCard(L.T("main.card.mode"), _modeValue, null, 10, 10), 1, 0);
        row.Controls.Add(CreateSummaryCard(L.T("main.card.blocked"), _blockedValue, _blockedDetail, 10, 0), 2, 0);
        return row;
    }

    private static Control CreateSummaryCard(string title, Label value, Label? detail, int left, int right)
    {
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(left, 0, right, 0) };
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(17, 11, 17, 10) };
        card.Controls.Add(new Label { Text = title, ForeColor = Theme.TextSecondary, Dock = DockStyle.Top, Height = 22 });
        value.Font = new Font("Segoe UI Semibold", 13F);
        value.ForeColor = Theme.TextPrimary;
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleLeft;
        value.AutoEllipsis = true;
        card.Controls.Add(value);
        if (detail is not null)
        {
            detail.ForeColor = Theme.TextSecondary;
            detail.Dock = DockStyle.Bottom;
            detail.Height = 22;
            detail.TextAlign = ContentAlignment.MiddleLeft;
            detail.AutoEllipsis = true;
            card.Controls.Add(detail);
        }
        value.BringToFront();
        outer.Controls.Add(card);
        return outer;
    }

    private Control BuildActions()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 6, 0, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        Theme.StyleButton(_monitoringButton, L.T("main.button.start"), true, 136);
        _monitoringButton.Margin = new Padding(0, 0, 14, 0);
        _monitoringButton.Click += (_, _) => { Program.ToggleMonitoring(); RefreshView(); };
        left.Controls.Add(_monitoringButton);

        _dryRunCheck.Text = L.T("main.dryRun");
        _dryRunCheck.AutoSize = true;
        _dryRunCheck.Margin = new Padding(0, 8, 0, 0);
        _dryRunCheck.CheckedChanged += DryRunChanged;
        left.Controls.Add(_dryRunCheck);
        layout.Controls.Add(left, 0, 0);

        var right = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0)
        };
        Theme.StyleButton(_openChromeButton, L.T("main.button.openChrome"), false, 120);
        _openChromeButton.Margin = new Padding(0, 0, 8, 0);
        _openChromeButton.Enabled = false;
        _openChromeButton.Click += (_, _) => { Program.RequestChromeLaunch(); RefreshView(); };
        right.Controls.Add(_openChromeButton);

        var openConfig = new Button();
        Theme.StyleButton(openConfig, L.T("main.button.settings"), false, 120);
        openConfig.Margin = new Padding(0, 0, 8, 0);
        openConfig.Click += (_, _) => ShowConfigDialog();
        right.Controls.Add(openConfig);

        // 닫기(X)는 설정에 따라 트레이로 숨으므로, 창에서 바로 완전히 끝낼 수 있는 버튼을 따로 둔다.
        var exit = new Button();
        Theme.StyleButton(exit, L.T("main.button.exit"), false, 90);
        exit.ForeColor = Theme.Error;
        exit.Margin = new Padding(0);
        exit.Click += (_, _) => ExitApplication();
        _toolTip.SetToolTip(exit, L.T("main.button.exit.tooltip"));
        right.Controls.Add(exit);
        layout.Controls.Add(right, 1, 0);
        return layout;
    }

    private Control BuildQuickOpen()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 4, 0, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _urlBox.Dock = DockStyle.Fill;
        _urlBox.Font = new Font("Segoe UI", 10.5F);
        _urlBox.PlaceholderText = L.T("main.url.placeholder");
        _urlBox.Margin = new Padding(0, 3, 8, 0);
        _urlBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            OpenTypedUrl();
        };
        layout.Controls.Add(_urlBox, 0, 0);

        var open = new Button();
        Theme.StyleButton(open, L.T("main.url.open"), false, 150);
        open.Margin = new Padding(0);
        open.Click += (_, _) => OpenTypedUrl();
        layout.Controls.Add(open, 1, 0);
        return layout;
    }

    private void OpenTypedUrl()
    {
        string text = _urlBox.Text.Trim();
        if (text.Length == 0)
            return;
        if (Program.OpenUrl(text))
            _urlBox.Clear();
    }

    private Control BuildRecentSection()
    {
        var group = CreateSection(L.T("main.recent.title"));

        _showKeptCheck.Text = L.T("main.recent.showKept");
        _showKeptCheck.AutoSize = true;
        _showKeptCheck.Checked = _uiState.ShowKept;
        _showKeptCheck.Margin = new Padding(0, 4, 0, 0);
        _showKeptCheck.CheckedChanged += (_, _) =>
        {
            _uiState.ShowKept = _showKeptCheck.Checked;
            _uiState.Save();
            RefreshView(true);
        };
        group.HeaderRight.Controls.Add(_showKeptCheck);

        // 상세 패널은 고정 높이로 두고 나머지를 목록에 준다.
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        group.Content.Controls.Add(split);

        _recentList.Dock = DockStyle.Fill;
        _recentList.BorderStyle = BorderStyle.None;
        _recentList.View = View.Details;
        _recentList.FullRowSelect = true;
        _recentList.HideSelection = false;
        _recentList.MultiSelect = false;
        _recentList.ShowItemToolTips = true;
        _recentList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _recentList.Columns.Add(L.T("main.recent.col.time"), 86);
        _recentList.Columns.Add(L.T("main.recent.col.kind"), 86);
        _recentList.Columns.Add(L.T("main.recent.col.count"), 52, HorizontalAlignment.Right);
        _recentList.Columns.Add(L.T("main.recent.col.score"), 52);
        _recentList.Columns.Add(L.T("main.recent.col.url"), 340);
        _recentList.Columns.Add(L.T("main.recent.col.reason"), 260);
        _recentList.SelectedIndexChanged += (_, _) => UpdateActionAvailability();
        _recentList.Margin = new Padding(0);
        split.Controls.Add(_recentList, 0, 0);
        Control detail = BuildDetailPanel();
        detail.Margin = new Padding(0, 6, 0, 0);
        split.Controls.Add(detail, 0, 1);

        Theme.StyleButton(_undoButton, L.T("main.recent.undo"), false, 110);
        Theme.StyleButton(_allowButton, L.T("main.recent.allow"), false, 110);
        Theme.StyleButton(_adDomainButton, L.T("main.recent.adDomain"), false, 110);
        Theme.StyleButton(_watchButton, L.T("main.recent.watch"), false, 110);
        _undoButton.Click += (_, _) => { if (SelectedOrLatest() is { } item) Program.Undo(item); RefreshView(true); };
        _allowButton.Click += (_, _) => { if (SelectedOrLatest() is { } item) Program.AllowSite(item); RefreshView(true); };
        _adDomainButton.Click += (_, _) => { if (SelectedItem() is { } item) Program.RegisterAdDomain(item); RefreshView(true); };
        _watchButton.Click += (_, _) => { if (SelectedItem() is { } item) Program.RegisterWatchedSite(item); RefreshView(true); };
        foreach (Button button in new[] { _undoButton, _allowButton, _adDomainButton, _watchButton })
        {
            button.Margin = new Padding(8, 0, 0, 0);
            group.Actions.Controls.Add(button);
        }
        return group.Root;
    }

    private Control BuildDetailPanel()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = Color.FromArgb(250, 251, 252)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));

        Label AddRow(int row, string label, Control value)
        {
            var caption = new Label
            {
                Text = label,
                ForeColor = Theme.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            table.Controls.Add(caption, 0, row);
            value.Dock = DockStyle.Fill;
            table.Controls.Add(value, 1, row);
            return caption;
        }

        foreach (TextBox box in new[] { _detailUrl, _detailOpener })
        {
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = table.BackColor;
            box.Margin = new Padding(3, 5, 3, 0);
        }
        _detailKind.TextAlign = ContentAlignment.MiddleLeft;
        _detailScore.TextAlign = ContentAlignment.MiddleLeft;
        _detailScore.AutoEllipsis = true;

        AddRow(0, L.T("main.detail.kind"), _detailKind);
        AddRow(1, L.T("main.detail.url"), _detailUrl);
        _detailOpenerCaption = AddRow(2, L.T("main.detail.opener"), _detailOpener);
        AddRow(3, L.T("main.detail.score"), _detailScore);
        return table;
    }

    private Control BuildActivitySection()
    {
        var group = CreateSection(L.T("main.activity.title"));
        _activity.Dock = DockStyle.Fill;
        _activity.BorderStyle = BorderStyle.None;
        _activity.BackColor = Theme.Surface;
        _activity.ForeColor = Theme.TextSecondary;
        _activity.ReadOnly = true;
        _activity.DetectUrls = false;
        group.Content.Controls.Add(_activity);

        var copyDiagnostics = new Button();
        Theme.StyleButton(copyDiagnostics, L.T("main.activity.diagnostics"), false, 110);
        copyDiagnostics.Margin = new Padding(8, 0, 0, 0);
        copyDiagnostics.Click += (_, _) => CopyDiagnostics();
        var openLogs = new Button();
        Theme.StyleButton(openLogs, L.T("main.activity.openLogs"), false, 110);
        openLogs.Margin = new Padding(8, 0, 0, 0);
        openLogs.Click += (_, _) => WindowsIntegration.OpenFolder(Program.DataDirectory);
        group.Actions.Controls.Add(copyDiagnostics);
        group.Actions.Controls.Add(openLogs);

        _scopeValue.AutoEllipsis = true;
        _scopeValue.ForeColor = Theme.TextSecondary;
        _scopeValue.Dock = DockStyle.Fill;
        _scopeValue.TextAlign = ContentAlignment.MiddleLeft;
        group.ActionsLeft.Controls.Add(_scopeValue);
        return group.Root;
    }

    private sealed record Section(
        Panel Root,
        Panel Content,
        FlowLayoutPanel Actions,
        Panel ActionsLeft,
        FlowLayoutPanel HeaderRight);

    private static Section CreateSection(string title)
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(16, 10, 16, 12), Margin = new Padding(0, 6, 0, 6) };

        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = Theme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var headerRight = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        header.Controls.Add(headerRight, 1, 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 42, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 6, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // 행 높이를 고정하지 않으면 Dock=Fill 패널이 기본 높이(100)로 커져 안의 문구가 잘려 보이지 않는다.
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actionsLeft = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        footer.Controls.Add(actionsLeft, 0, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0)
        };
        footer.Controls.Add(actions, 1, 0);

        var content = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(content);
        root.Controls.Add(footer);
        root.Controls.Add(header);
        return new Section(root, content, actions, actionsLeft, headerRight);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        var show = new ToolStripMenuItem(L.T("tray.show"), null, (_, _) => RestoreFromTray());
        show.Font = new Font(show.Font, FontStyle.Bold);
        _trayMonitoring.Text = L.T("tray.monitoring");
        _trayMonitoring.Click += (_, _) => { Program.ToggleMonitoring(); RefreshView(); };
        _trayDryRun.Text = L.T("tray.dryRun");
        _trayDryRun.Click += (_, _) => { Program.SetDryRun(!Program.CurrentConfig.DryRun); RefreshView(); };
        _trayOpenChrome.Text = L.T("tray.openChrome");
        _trayOpenChrome.Click += (_, _) => Program.RequestChromeLaunch();
        var openLogs = new ToolStripMenuItem(L.T("tray.openLogs"), null,
            (_, _) => WindowsIntegration.OpenFolder(Program.DataDirectory));
        var exit = new ToolStripMenuItem(L.T("tray.exit"), null, (_, _) => ExitApplication());

        menu.Items.AddRange(new ToolStripItem[]
        {
            show, new ToolStripSeparator(), _trayMonitoring, _trayDryRun, _trayOpenChrome,
            openLogs, new ToolStripSeparator(), exit
        });
        menu.Opening += (_, _) => UpdateTrayMenu(Program.GetSnapshot());

        _tray.Icon = Theme.AppIcon ?? SystemIcons.Application;
        _tray.Text = "TabBouncer";
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        _tray.BalloonTipClicked += (_, _) => RestoreFromTray();
        _tray.Visible = true;
    }

    private void UpdateTrayMenu(AppSnapshot snapshot)
    {
        _trayMonitoring.Checked = snapshot.Enabled;
        _trayDryRun.Checked = snapshot.DryRun;
        _trayOpenChrome.Enabled = snapshot.CanOpenChrome;
    }

    private void OnResized(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            if (Program.CurrentConfig.MinimizeToTray && Visible)
                HideToTray(showHint: false);
            return;
        }
        _lastVisibleState = WindowState;
    }

    private void HideToTray(bool showHint)
    {
        Hide();
        if (showHint && !_uiState.TrayHintShown)
        {
            _uiState.TrayHintShown = true;
            _uiState.Save();
            _tray.ShowBalloonTip(3000, "TabBouncer", L.T("tray.hint"), ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        if (IsDisposed)
            return;
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = _lastVisibleState;
        Activate();
        BringToFront();
    }

    private void OnShowRequested()
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(new Action(RestoreFromTray));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (Program.CurrentConfig.CloseToTray)
                HideToTray(showHint: true);
            else
                BeginInvoke(new Action(ExitApplication));
            return;
        }

        SaveUiState();
    }

    // 창의 "종료" 버튼, 트레이 메뉴의 "종료", Ctrl+Q, (트레이로 숨기기를 끈 경우) 닫기 버튼이 모두 이 흐름을 탄다.
    private void ExitApplication()
    {
        if (_exiting)
            return;

        bool closeChrome = false;
        if (Program.GetSnapshot().Connected)
        {
            string choice = Program.CurrentConfig.CloseChromeOnExit;
            if (choice == "ask")
            {
                using var dialog = new ExitDialog();
                if (dialog.ShowDialog(Visible ? this : null) != DialogResult.OK)
                    return;
                closeChrome = dialog.CloseChrome;
                if (dialog.Remember)
                    Program.SetCloseChromeOnExit(closeChrome ? "always" : "never");
            }
            else
            {
                closeChrome = choice == "always";
            }
        }

        _exiting = true;
        if (closeChrome)
        {
            try
            {
                Task.Run(Program.CloseChromeAsync).Wait(TimeSpan.FromSeconds(4));
            }
            catch (AggregateException)
            {
            }
        }
        Close();
    }

    private void SaveUiState()
    {
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _uiState.X = bounds.X;
        _uiState.Y = bounds.Y;
        _uiState.Width = bounds.Width;
        _uiState.Height = bounds.Height;
        _uiState.Maximized = WindowState == FormWindowState.Maximized ||
                             (WindowState == FormWindowState.Minimized && _lastVisibleState == FormWindowState.Maximized);
        _uiState.Save();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool typing = ActiveControl is TextBoxBase;
        switch (keyData)
        {
            case Keys.Control | Keys.M:
                Program.ToggleMonitoring();
                RefreshView();
                return true;
            case Keys.Control | Keys.D:
                Program.SetDryRun(!Program.CurrentConfig.DryRun);
                RefreshView();
                return true;
            case Keys.Control | Keys.O:
                Program.RequestChromeLaunch();
                RefreshView();
                return true;
            case Keys.Control | Keys.Z when !typing:
                if (SelectedOrLatest() is { } item)
                    Program.Undo(item);
                RefreshView(true);
                return true;
            case Keys.Control | Keys.Oemcomma:
                ShowConfigDialog();
                return true;
            case Keys.Control | Keys.L:
                _urlBox.Focus();
                _urlBox.SelectAll();
                return true;
            case Keys.Control | Keys.Q:
                ExitApplication();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RefreshView(bool forceRecent = false)
    {
        if (IsDisposed)
            return;

        AppSnapshot snapshot = Program.GetSnapshot();
        _connectionDot.ForeColor = snapshot.Connected ? Theme.Success : Theme.Warning;
        _connectionText.Text = snapshot.Connected && snapshot.BrowserName.Length > 0
            ? L.Format("status.connectedWith", snapshot.BrowserName)
            : snapshot.ConnectionStatus;
        _monitoringValue.Text = snapshot.Enabled ? L.T("main.state.on") : L.T("main.state.off");
        _monitoringValue.ForeColor = snapshot.Enabled ? Theme.Success : Theme.Warning;
        _modeValue.Text = snapshot.DryRun ? L.T("main.mode.dryRun") : L.T("main.mode.live");
        _blockedValue.Text = L.Format("main.blocked.session", snapshot.SessionClosedCount);
        _blockedDetail.Text = L.Format("main.blocked.total", snapshot.TotalClosedCount);
        _scopeValue.Text = L.Format("main.scope", snapshot.WatchedSites, snapshot.CloseThreshold);
        _monitoringButton.Text = snapshot.Enabled ? L.T("main.button.pause") : L.T("main.button.start");
        _openChromeButton.Enabled = snapshot.CanOpenChrome;
        UpdateBanner(snapshot);
        UpdateTrayMenu(snapshot);

        string trayText = snapshot.Enabled
            ? (snapshot.Connected ? L.T("tray.tooltip.on") : L.T("tray.tooltip.chromeClosed"))
            : L.T("tray.tooltip.off");
        trayText = "TabBouncer - " + trayText;
        _tray.Text = trayText.Length > 120 ? trayText[..120] : trayText;

        if (_dryRunCheck.Checked != snapshot.DryRun)
        {
            _dryRunCheck.CheckedChanged -= DryRunChanged;
            _dryRunCheck.Checked = snapshot.DryRun;
            _dryRunCheck.CheckedChanged += DryRunChanged;
        }

        RefreshRecentList(forceRecent);
        UpdateActionAvailability();
    }

    private void UpdateBanner(AppSnapshot snapshot)
    {
        string text;
        Color back = Theme.OffBannerBackground;
        Color fore = Theme.OffBannerText;
        string title = "TabBouncer";
        if (!snapshot.Enabled)
        {
            text = L.T("main.banner.off");
            title = "TabBouncer - " + L.T("main.title.off");
        }
        else if (!snapshot.Connected)
        {
            text = snapshot.CanOpenChrome ? L.T("main.banner.chromeClosed") : L.T("main.banner.connecting");
            back = Theme.ClosedBannerBackground;
            fore = Theme.ClosedBannerText;
            if (snapshot.CanOpenChrome)
                title = "TabBouncer - " + L.T("main.title.chromeClosed");
        }
        else
        {
            text = "";
        }

        bool showBanner = text.Length > 0;
        if (_monitoringBanner.Text != text)
            _monitoringBanner.Text = text;
        _monitoringBanner.BackColor = back;
        _monitoringBanner.ForeColor = fore;
        if (_monitoringBanner.Visible != showBanner)
        {
            _monitoringBanner.Visible = showBanner;
            _bannerRow.Height = showBanner ? BannerHeight : 0;
        }
        if (Text != title)
            Text = title;
    }

    private void RefreshRecentList(bool force)
    {
        bool showKept = _showKeptCheck.Checked;
        ClosedItem[] items = Program.GetRecentClosed()
            .Where(item => showKept || item.Kind is ClosedKind.ClosedTab or ClosedKind.RevertedRedirect)
            .ToArray();
        string signature = showKept + "|" + string.Join("|", items.Select(item => item.Signature));
        if (!force && signature == _recentSignature)
            return;

        _recentSignature = signature;
        // 반복으로 횟수가 늘면 항목 인스턴스가 바뀌므로, 선택은 묶음 기준으로 유지한다.
        string? selectedKey = SelectedItem()?.GroupKey;
        _recentList.BeginUpdate();
        _recentList.Items.Clear();
        foreach (ClosedItem item in items)
        {
            string time = item.At.Date == DateTime.Today
                ? item.At.ToString("HH:mm:ss")
                : item.At.ToString("MM-dd HH:mm");
            var row = new ListViewItem(time);
            row.SubItems.Add(Reasons.KindLabel(item.Kind));
            row.SubItems.Add(item.Count > 1 ? L.Format("main.recent.countValue", item.Count) : "1");
            row.SubItems.Add(item.Score.ToString());
            row.SubItems.Add(item.Url);
            row.SubItems.Add(Reasons.Describe(item.Reason));
            row.Tag = item;
            row.ToolTipText = BuildTooltip(item);
            if (item.Restored)
                row.ForeColor = Theme.TextMuted;
            else if (item.Kind is ClosedKind.Kept or ClosedKind.Observed)
                row.ForeColor = Theme.TextSecondary;
            if (item.GroupKey == selectedKey)
                row.Selected = true;
            _recentList.Items.Add(row);
        }
        _recentList.EndUpdate();
        UpdateDetail();
    }

    private static string BuildTooltip(ClosedItem item)
    {
        string lines = string.Join(Environment.NewLine, item.Breakdown.Select(part =>
            $"{part.Points,4:+#;-#;0}  {Reasons.One(part.Code)}"));
        string header = $"{Reasons.KindLabel(item.Kind)} · {item.Score}  [{item.Reason}]";
        if (item.Count > 1)
            header += Environment.NewLine + L.Format("main.detail.repeated", item.Count, item.FirstSeen.ToString("HH:mm:ss"));
        return lines.Length == 0 ? header : header + Environment.NewLine + lines;
    }

    private void DryRunChanged(object? sender, EventArgs e) => Program.SetDryRun(_dryRunCheck.Checked);

    private ClosedItem? SelectedItem() =>
        _recentList.SelectedItems.Count > 0 ? _recentList.SelectedItems[0].Tag as ClosedItem : null;

    // 선택한 항목이 있으면 그 항목에, 없으면 목록에서 가장 최근에 닫거나 되돌린 항목에 적용한다.
    private ClosedItem? SelectedOrLatest() =>
        SelectedItem() ?? _recentList.Items.Cast<ListViewItem>()
            .Select(row => row.Tag as ClosedItem)
            .FirstOrDefault(item => item?.Kind is ClosedKind.ClosedTab or ClosedKind.RevertedRedirect);

    private void UpdateActionAvailability()
    {
        ClosedItem? selected = SelectedItem();
        ClosedItem? target = SelectedOrLatest();
        _undoButton.Enabled = target?.Kind is ClosedKind.ClosedTab or ClosedKind.RevertedRedirect;
        _allowButton.Enabled = target is not null;
        _adDomainButton.Enabled = selected is not null && Program.SiteOf(selected.Url).Length > 0;
        _watchButton.Enabled = selected is not null && Program.SiteOf(selected.OpenerUrl).Length > 0;

        string undoText = selected is not null ? L.T("main.recent.undoSelected") : L.T("main.recent.undo");
        string allowText = selected is not null ? L.T("main.recent.allowSelected") : L.T("main.recent.allow");
        if (_undoButton.Text != undoText)
            _undoButton.Text = undoText;
        if (_allowButton.Text != allowText)
            _allowButton.Text = allowText;
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        ClosedItem? item = SelectedItem();
        if (item is null)
        {
            _detailKind.Text = L.T("main.detail.none");
            _detailKind.ForeColor = Theme.TextMuted;
            _detailUrl.Text = "";
            _detailOpener.Text = "";
            _detailScore.Text = "";
            return;
        }

        _detailKind.ForeColor = Theme.TextPrimary;
        _detailKind.Text = $"{Reasons.KindLabel(item.Kind)} · {item.At:yyyy-MM-dd HH:mm:ss}" +
                           (item.Count > 1
                               ? " · " + L.Format("main.detail.repeated", item.Count, item.FirstSeen.ToString("HH:mm:ss"))
                               : "") +
                           (item.Restored ? " · " + L.T("main.detail.restored") : "");
        if (_detailUrl.Text != item.Url)
            _detailUrl.Text = item.Url;
        if (_detailOpener.Text != item.OpenerUrl)
            _detailOpener.Text = item.OpenerUrl;
        if (_detailOpenerCaption is not null)
        {
            // 리다이렉트 항목의 두 번째 주소는 탭을 연 곳이 아니라 되돌아간 원래 페이지다.
            string caption = item.Kind == ClosedKind.RevertedRedirect
                ? L.T("main.detail.restoredUrl")
                : L.T("main.detail.opener");
            if (_detailOpenerCaption.Text != caption)
                _detailOpenerCaption.Text = caption;
        }
        _detailScore.Text = item.Breakdown.Count == 0
            ? $"{item.Score} · {Reasons.Describe(item.Reason)}"
            : $"{item.Score} = " + string.Join("  ", item.Breakdown.Select(part =>
                $"{part.Points:+#;-#;0} {Reasons.One(part.Code)}"));
    }

    private void OnItemRecorded(ClosedItem item)
    {
        if (item.Kind is not (ClosedKind.ClosedTab or ClosedKind.RevertedRedirect) ||
            !Program.CurrentConfig.NotifyOnBlock || IsDisposed || !IsHandleCreated)
            return;
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() => OnItemRecorded(item)));
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }

        _pendingNotifications++;
        _lastBlockedHost = Program.HostForDisplay(item.Url);
        if (!_notifyTimer.Enabled)
        {
            FlushNotification();
            _notifyTimer.Start();
        }
    }

    // 첫 알림은 바로 띄우고, 3초 안에 이어진 차단은 한 번에 묶어 알린다.
    private void FlushNotification()
    {
        if (_pendingNotifications == 0)
            return;
        string text = _pendingNotifications == 1
            ? L.Format("notify.one", _lastBlockedHost)
            : L.Format("notify.many", _pendingNotifications);
        _tray.ShowBalloonTip(2500, "TabBouncer", text, ToolTipIcon.Info);
        _pendingNotifications = 0;
    }

    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(Program.BuildDiagnostics());
            MessageBox.Show(this, L.T("main.activity.diagnosticsCopied"), "TabBouncer",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TabBouncer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnLogEmitted(AppLogEntry entry)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => OnLogEmitted(entry))); }
            catch (InvalidOperationException) { }
            return;
        }

        Color color = entry.Level switch
        {
            "success" => Theme.Success,
            "warning" => Theme.Warning,
            "error" => Theme.Error,
            _ => Theme.TextSecondary
        };
        _activity.SelectionStart = _activity.TextLength;
        _activity.SelectionColor = color;
        _activity.AppendText($"[{entry.At:HH:mm:ss}] {entry.Message}{Environment.NewLine}");
        while (_activity.Lines.Length > 200)
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
        if (!Visible)
            RestoreFromTray();
        using var dialog = new ConfigForm();
        dialog.ShowDialog(this);
        RefreshView(true);
    }
}
