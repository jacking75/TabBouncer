#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TabBouncer;

// config.json을 일반 설정, 사이트 목록, JSON 원문 세 탭으로 편집한다.
// 편집은 복사본에서 하고 "저장"을 눌러야 검증 뒤 파일에 쓰고 바로 다시 적용한다.
internal sealed class ConfigForm : Form
{
    private sealed record ListCategory(
        string Key,
        Func<Config, List<string>> Get,
        Func<string, string> Normalize,
        bool ReadOnly = false,
        bool IsUrlList = false);

    private Config _model;
    private readonly string _originalLanguage;
    private readonly bool _originalAutoRun;
    private readonly TabControl _tabs = new();
    private readonly TabPage _generalPage = new();
    private readonly TabPage _sitesPage = new();
    private readonly TabPage _jsonPage = new();
    private readonly TextBox _editor = new();
    private readonly Label _statusLabel = new();

    private readonly CheckBox _dryRun = new();
    private readonly CheckBox _protectClicks = new();
    private readonly CheckBox _blockPopups = new();
    private readonly CheckBox _blockRedirect = new();
    private readonly CheckBox _preemptive = new();
    private readonly CheckBox _refocus = new();
    private readonly CheckBox _strict = new();
    private readonly CheckBox _builtinAds = new();
    private readonly NumericUpDown _threshold = new();
    private readonly NumericUpDown _debounce = new();
    private readonly NumericUpDown _intentWindow = new();
    private readonly CheckBox _startMonitoring = new();
    private readonly CheckBox _autoRun = new();
    private readonly CheckBox _minimizeToTray = new();
    private readonly CheckBox _closeToTray = new();
    private readonly CheckBox _notify = new();
    private readonly ComboBox _closeChrome = new();
    private readonly ComboBox _language = new();
    private readonly TextBox _chromePath = new();
    private readonly TextBox _startUrl = new();
    private readonly TextBox _userDataDir = new();
    private readonly NumericUpDown _debugPort = new();
    private readonly CheckBox _autoLaunch = new();

    private readonly ComboBox _listSelector = new();
    private readonly Label _listDescription = new();
    private readonly ListBox _listItems = new();
    private readonly TextBox _listInput = new();
    private readonly Button _listAdd = new();
    private readonly Button _listRemove = new();
    private readonly Button _listShortcut = new();
    private readonly Panel _addBorder = new();
    private readonly TableLayoutPanel _addArea = new();
    private readonly Label _addTitle = new();
    private readonly Panel _inputBorder = new();
    private readonly Label _listCount = new();
    private readonly List<ListCategory> _categories;

    private static readonly Color AddAreaBackground = Color.FromArgb(239, 246, 255);
    private static readonly Color AddAreaTitle = Color.FromArgb(30, 64, 175);

    internal ConfigForm()
    {
        _model = Program.GetEditableConfig();
        _originalLanguage = _model.Language;
        _originalAutoRun = WindowsIntegration.IsAutoRunEnabled();

        _categories = new()
        {
            new("allowedSites", c => c.AllowedSites, Config.NormalizeDomain),
            new("watchedSites", c => c.WatchedSites, Config.NormalizeDomain),
            new("adDomains", c => c.AdDomains, Config.NormalizeDomain),
            new("removedAdDomains", c => c.RemovedAdDomains, Config.NormalizeDomain),
            new("builtinAdDomains", _ => Config.BuiltinAdDomains.ToList(), Config.NormalizeDomain, ReadOnly: true),
            new("whitelist", c => c.Whitelist, Config.NormalizeDomain),
            new("suspiciousTlds", c => c.SuspiciousTlds, value => value.Trim().TrimStart('.').ToLowerInvariant()),
            new("favoriteSites", c => c.FavoriteSites, Config.NormalizeUrl, IsUrlList: true)
        };

        Text = L.T("config.title");
        Icon = Theme.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(720, 620);
        Size = new Size(820, 720);
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(new Label
        {
            Text = Program.GetConfigPath(),
            ForeColor = Theme.TextSecondary,
            AutoEllipsis = true,
            Dock = DockStyle.Fill
        }, 0, 0);

        _generalPage.Text = L.T("config.tab.general");
        _sitesPage.Text = L.T("config.tab.sites");
        _jsonPage.Text = L.T("config.tab.json");
        _generalPage.Controls.Add(BuildGeneralPage());
        _sitesPage.Controls.Add(BuildSitesPage());
        _jsonPage.Controls.Add(BuildJsonPage());
        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.AddRange(new[] { _generalPage, _sitesPage, _jsonPage });
        _tabs.Selecting += OnTabSelecting;
        // 사이트 목록 탭으로 오면 바로 입력할 수 있게 입력칸에 커서를 둔다.
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == _sitesPage && _listInput.Enabled)
                BeginInvoke(new Action(() => _listInput.Focus()));
        };
        root.Controls.Add(_tabs, 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Theme.Error;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        Controls.Add(root);

        LoadModelIntoControls();
    }

    private Control BuildButtons()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var reset = new Button();
        Theme.StyleButton(reset, L.T("config.button.defaults"), false, 130);
        reset.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        reset.Margin = new Padding(0, 6, 0, 0);
        reset.Click += (_, _) => RestoreDefaults();
        bar.Controls.Add(reset, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0)
        };
        var save = new Button();
        Theme.StyleButton(save, L.T("config.button.save"), true);
        save.Margin = new Padding(8, 0, 0, 0);
        save.Click += (_, _) => Save();
        var cancel = new Button();
        Theme.StyleButton(cancel, L.T("common.cancel"), false);
        cancel.Margin = new Padding(8, 0, 0, 0);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        bar.Controls.Add(buttons, 1, 0);
        CancelButton = cancel;
        return bar;
    }

    private Control BuildGeneralPage()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            Padding = new Padding(14, 8, 24, 14),
            BackColor = Theme.Surface
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddHeading(table, L.T("config.group.judge"));
        AddCheck(table, _dryRun, "dryRun");
        AddCheck(table, _protectClicks, "protectExplicitClicks");
        AddCheck(table, _blockPopups, "blockAutomaticCrossSitePopups");
        AddCheck(table, _blockRedirect, "blockRedirectHijack");
        AddCheck(table, _preemptive, "preemptiveBlock");
        AddCheck(table, _refocus, "refocusOpener");
        AddCheck(table, _builtinAds, "useBuiltinAdDomains");
        AddCheck(table, _strict, "strictMode");
        AddNumber(table, _threshold, "closeThreshold", 1, 300);
        AddNumber(table, _debounce, "debounceMs", 200, 10000);
        AddNumber(table, _intentWindow, "intentWindowMs", 500, 20000);

        AddHeading(table, L.T("config.group.app"));
        AddCheck(table, _startMonitoring, "startMonitoringOnLaunch");
        AddCheck(table, _autoRun, "autoRun");
        AddCheck(table, _minimizeToTray, "minimizeToTray");
        AddCheck(table, _closeToTray, "closeToTray");
        AddCheck(table, _notify, "notifyOnBlock");
        _closeChrome.DropDownStyle = ComboBoxStyle.DropDownList;
        _closeChrome.Items.AddRange(new object[]
        {
            L.T("config.closeChrome.ask"), L.T("config.closeChrome.always"), L.T("config.closeChrome.never")
        });
        AddField(table, _closeChrome, "closeChromeOnExit", 220);
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Items.AddRange(new object[] { L.T("config.language.system"), "한국어", "English" });
        AddField(table, _language, "language", 220);

        AddHeading(table, L.T("config.group.browser"));
        var browse = new Button();
        Theme.StyleButton(browse, L.T("config.button.browse"), false, 90);
        browse.Margin = new Padding(6, 0, 0, 0);
        browse.Click += (_, _) => BrowseChrome();
        AddField(table, _chromePath, "chromePath", 460, browse);
        AddField(table, _startUrl, "startUrl", 460);
        AddField(table, _userDataDir, "userDataDir", 460);
        AddNumber(table, _debugPort, "debugPort", 0, 65535);
        AddCheck(table, _autoLaunch, "autoLaunchChrome");
        return table;
    }

    private static void AddHeading(TableLayoutPanel table, string text)
    {
        table.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5F),
            Margin = new Padding(0, table.Controls.Count == 0 ? 4 : 18, 0, 6)
        });
    }

    private static Label Description(string key) => new()
    {
        Text = L.T("config.field." + key + ".help"),
        AutoSize = true,
        MaximumSize = new Size(640, 0),
        ForeColor = Theme.TextSecondary,
        Margin = new Padding(20, 0, 0, 8)
    };

    private static void AddCheck(TableLayoutPanel table, CheckBox check, string key)
    {
        check.Text = L.T("config.field." + key);
        check.AutoSize = true;
        check.Margin = new Padding(0, 2, 0, 0);
        table.Controls.Add(check);
        table.Controls.Add(Description(key));
    }

    private static void AddNumber(TableLayoutPanel table, NumericUpDown number, string key, int minimum, int maximum)
    {
        number.Minimum = minimum;
        number.Maximum = maximum;
        number.Width = 100;
        number.TextAlign = HorizontalAlignment.Right;
        AddField(table, number, key, 100);
    }

    private static void AddField(TableLayoutPanel table, Control input, string key, int width, Control? extra = null)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        row.Controls.Add(new Label
        {
            Text = L.T("config.field." + key),
            AutoSize = false,
            Width = 190,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        });
        input.Width = width;
        input.Margin = new Padding(0, 2, 0, 0);
        row.Controls.Add(input);
        if (extra is not null)
            row.Controls.Add(extra);
        table.Controls.Add(row);
        Label help = Description(key);
        help.Margin = new Padding(190, 0, 0, 8);
        table.Controls.Add(help);
    }

    private Control BuildSitesPage()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(14, 10, 14, 12),
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowCount = 5;

        _listSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _listSelector.Width = 360;
        foreach (ListCategory category in _categories)
            _listSelector.Items.Add(L.T("config.list." + category.Key));
        _listSelector.SelectedIndexChanged += (_, _) => ShowSelectedList();
        layout.Controls.Add(_listSelector, 0, 0);
        layout.SetColumnSpan(_listSelector, 2);

        _listDescription.AutoSize = true;
        _listDescription.MaximumSize = new Size(700, 0);
        _listDescription.ForeColor = Theme.TextSecondary;
        _listDescription.Margin = new Padding(0, 2, 0, 8);
        layout.Controls.Add(_listDescription, 0, 1);
        layout.SetColumnSpan(_listDescription, 2);

        Control addArea = BuildAddArea();
        layout.Controls.Add(addArea, 0, 2);
        layout.SetColumnSpan(addArea, 2);

        _listCount.Dock = DockStyle.Fill;
        _listCount.TextAlign = ContentAlignment.BottomLeft;
        _listCount.ForeColor = Theme.TextSecondary;
        _listCount.Font = new Font("Segoe UI Semibold", 9F);
        _listCount.Margin = new Padding(0, 0, 0, 4);
        layout.Controls.Add(_listCount, 0, 3);
        layout.SetColumnSpan(_listCount, 2);

        _listItems.Dock = DockStyle.Fill;
        _listItems.IntegralHeight = false;
        _listItems.SelectionMode = SelectionMode.MultiExtended;
        _listItems.SelectedIndexChanged += (_, _) => UpdateListButtons();
        _listItems.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
                RemoveSelectedListItems();
        };
        layout.Controls.Add(_listItems, 0, 4);

        var side = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(8, 0, 0, 0)
        };
        Theme.StyleButton(_listRemove, L.T("config.list.remove"), false, 150);
        _listRemove.Margin = new Padding(0, 0, 0, 8);
        _listRemove.Click += (_, _) => RemoveSelectedListItems();
        Theme.StyleButton(_listShortcut, L.T("config.list.shortcut"), false, 150);
        _listShortcut.Margin = new Padding(0);
        _listShortcut.Click += (_, _) => CreateShortcuts();
        side.Controls.Add(_listRemove);
        side.Controls.Add(_listShortcut);
        layout.Controls.Add(side, 1, 4);
        return layout;
    }

    // 새 항목 입력칸은 목록 위에 파란 테두리 영역으로 둬, 목록과 헷갈리지 않고 먼저 눈에 띄게 한다.
    private Control BuildAddArea()
    {
        _addBorder.Dock = DockStyle.Fill;
        _addBorder.BackColor = Theme.Primary;
        _addBorder.Padding = new Padding(1);
        _addBorder.Margin = new Padding(0, 0, 0, 10);

        _addArea.Dock = DockStyle.Fill;
        _addArea.BackColor = AddAreaBackground;
        _addArea.Padding = new Padding(14, 8, 14, 12);
        _addArea.ColumnCount = 2;
        _addArea.RowCount = 2;
        _addArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _addArea.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _addArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _addArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _addBorder.Controls.Add(_addArea);

        _addTitle.Dock = DockStyle.Fill;
        _addTitle.TextAlign = ContentAlignment.MiddleLeft;
        _addTitle.Font = new Font("Segoe UI Semibold", 10F);
        _addTitle.ForeColor = AddAreaTitle;
        _addTitle.Margin = new Padding(0);
        _addArea.Controls.Add(_addTitle, 0, 0);
        _addArea.SetColumnSpan(_addTitle, 2);

        // TextBox 자체 테두리는 흐리게 보이므로, 파란 테두리 패널 안에 흰 바탕과 여백을 두고 테두리 없는 입력칸을 넣는다.
        _inputBorder.Dock = DockStyle.Fill;
        _inputBorder.BackColor = Theme.Primary;
        _inputBorder.Padding = new Padding(2);
        _inputBorder.Margin = new Padding(0, 2, 10, 0);
        var inputBack = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Cursor = Cursors.IBeam };
        _inputBorder.Controls.Add(inputBack);

        _listInput.BorderStyle = BorderStyle.None;
        _listInput.Font = new Font("Segoe UI", 11F);
        _listInput.BackColor = Theme.Surface;
        _listInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            AddListItem();
        };
        inputBack.Controls.Add(_listInput);
        inputBack.Resize += (_, _) => _listInput.SetBounds(
            10, Math.Max(0, (inputBack.ClientSize.Height - _listInput.Height) / 2),
            Math.Max(10, inputBack.ClientSize.Width - 20), _listInput.Height);
        inputBack.Click += (_, _) => _listInput.Focus();
        _addArea.Controls.Add(_inputBorder, 0, 1);

        Theme.StyleButton(_listAdd, L.T("config.list.add"), true, 120);
        _listAdd.MinimumSize = new Size(120, 42);
        _listAdd.Margin = new Padding(0, 2, 0, 0);
        _listAdd.Dock = DockStyle.Fill;
        _listAdd.Click += (_, _) => AddListItem();
        _addArea.Controls.Add(_listAdd, 1, 1);
        return _addBorder;
    }

    private Control BuildJsonPage()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Theme.Surface };
        _editor.Multiline = true;
        _editor.ScrollBars = ScrollBars.Both;
        _editor.WordWrap = false;
        _editor.AcceptsTab = true;
        _editor.AcceptsReturn = true;
        _editor.BackColor = Theme.Surface;
        _editor.Font = new Font("Consolas", 10F);
        _editor.Dock = DockStyle.Fill;
        panel.Controls.Add(_editor);
        panel.Controls.Add(new Label
        {
            Text = L.T("config.json.help"),
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Theme.TextSecondary
        });
        return panel;
    }

    private void LoadModelIntoControls()
    {
        Config c = _model;
        _dryRun.Checked = c.DryRun;
        _protectClicks.Checked = c.ProtectExplicitClicks;
        _blockPopups.Checked = c.BlockAutomaticCrossSitePopups;
        _blockRedirect.Checked = c.BlockRedirectHijack;
        _preemptive.Checked = c.PreemptiveBlock;
        _refocus.Checked = c.RefocusOpener;
        _strict.Checked = c.StrictMode;
        _builtinAds.Checked = c.UseBuiltinAdDomains;
        _threshold.Value = Clamp(c.CloseThreshold, _threshold);
        _debounce.Value = Clamp(c.DebounceMs, _debounce);
        _intentWindow.Value = Clamp(c.IntentWindowMs, _intentWindow);
        _startMonitoring.Checked = c.StartMonitoringOnLaunch;
        _autoRun.Checked = _originalAutoRun;
        _autoRun.Enabled = !Program.UsesDataDirectoryArgument;
        _minimizeToTray.Checked = c.MinimizeToTray;
        _closeToTray.Checked = c.CloseToTray;
        _notify.Checked = c.NotifyOnBlock;
        _closeChrome.SelectedIndex = Math.Max(0, Array.IndexOf(Config.CloseChromeChoices, c.CloseChromeOnExit));
        _language.SelectedIndex = Math.Max(0, Array.IndexOf(Config.LanguageChoices, c.Language));
        _chromePath.Text = c.ChromePath;
        _chromePath.PlaceholderText = L.T("config.field.chromePath.placeholder");
        _startUrl.Text = c.StartUrl;
        _startUrl.PlaceholderText = L.T("config.field.startUrl.placeholder");
        _userDataDir.Text = c.UserDataDir;
        _userDataDir.PlaceholderText = Path.Combine(Program.DataDirectory, "ChromeProfile");
        _debugPort.Value = Clamp(c.DebugPort, _debugPort);
        _autoLaunch.Checked = c.AutoLaunchChrome;

        if (_listSelector.SelectedIndex < 0)
            _listSelector.SelectedIndex = 0;
        else
            ShowSelectedList();
        _editor.Text = Program.SerializeConfig(_model).Replace("\n", Environment.NewLine);
    }

    private static decimal Clamp(int value, NumericUpDown number) =>
        Math.Min(number.Maximum, Math.Max(number.Minimum, value));

    // 목록은 편집 즉시 _model에 반영되고, 일반 탭 컨트롤은 저장·탭 이동 때 모은다.
    private void ReadControlsIntoModel()
    {
        Config c = _model;
        c.DryRun = _dryRun.Checked;
        c.ProtectExplicitClicks = _protectClicks.Checked;
        c.BlockAutomaticCrossSitePopups = _blockPopups.Checked;
        c.BlockRedirectHijack = _blockRedirect.Checked;
        c.PreemptiveBlock = _preemptive.Checked;
        c.RefocusOpener = _refocus.Checked;
        c.StrictMode = _strict.Checked;
        c.UseBuiltinAdDomains = _builtinAds.Checked;
        c.CloseThreshold = (int)_threshold.Value;
        c.DebounceMs = (int)_debounce.Value;
        c.IntentWindowMs = (int)_intentWindow.Value;
        c.StartMonitoringOnLaunch = _startMonitoring.Checked;
        c.MinimizeToTray = _minimizeToTray.Checked;
        c.CloseToTray = _closeToTray.Checked;
        c.NotifyOnBlock = _notify.Checked;
        c.CloseChromeOnExit = Config.CloseChromeChoices[Math.Max(0, _closeChrome.SelectedIndex)];
        c.Language = Config.LanguageChoices[Math.Max(0, _language.SelectedIndex)];
        c.ChromePath = _chromePath.Text.Trim();
        c.StartUrl = _startUrl.Text.Trim();
        c.UserDataDir = _userDataDir.Text.Trim();
        c.DebugPort = (int)_debugPort.Value;
        c.AutoLaunchChrome = _autoLaunch.Checked;
        c.Resolve();
    }

    private void OnTabSelecting(object? sender, TabControlCancelEventArgs e)
    {
        _statusLabel.Text = "";
        if (_tabs.SelectedTab == _jsonPage && e.TabPage != _jsonPage)
        {
            // JSON 탭을 떠날 때 원문을 해석해 다른 탭에 반영한다. 해석하지 못하면 이동을 막는다.
            Config? parsed = Program.ParseConfig(_editor.Text, out string error);
            if (parsed is null)
            {
                e.Cancel = true;
                _statusLabel.Text = L.Format("config.error.json", error);
                return;
            }
            _model = parsed;
            LoadModelIntoControls();
        }
        else if (e.TabPage == _jsonPage)
        {
            ReadControlsIntoModel();
            _editor.Text = Program.SerializeConfig(_model).Replace("\n", Environment.NewLine);
        }
    }

    private ListCategory? SelectedCategory =>
        _listSelector.SelectedIndex >= 0 ? _categories[_listSelector.SelectedIndex] : null;

    private void ShowSelectedList()
    {
        if (SelectedCategory is not { } category)
            return;
        _listDescription.Text = L.T("config.list." + category.Key + ".help");
        _listItems.BeginUpdate();
        _listItems.Items.Clear();
        foreach (string value in category.Get(_model))
            _listItems.Items.Add(value);
        _listItems.EndUpdate();
        _listCount.Text = L.Format("config.list.count", _listItems.Items.Count);
        _listInput.Enabled = !category.ReadOnly;
        _listAdd.Enabled = !category.ReadOnly;
        _addTitle.Text = L.T(category.ReadOnly ? "config.list.readonlyTitle" : "config.list.addTitle");
        _addBorder.BackColor = category.ReadOnly ? Theme.Border : Theme.Primary;
        _inputBorder.BackColor = category.ReadOnly ? Theme.Border : Theme.Primary;
        _addArea.BackColor = category.ReadOnly ? Theme.Background : AddAreaBackground;
        _addTitle.ForeColor = category.ReadOnly ? Theme.TextSecondary : AddAreaTitle;
        _listInput.PlaceholderText = category.ReadOnly
            ? ""
            : L.T(category.IsUrlList ? "config.list.placeholder.url" : "config.list.placeholder.domain");
        _listShortcut.Visible = category.IsUrlList;
        UpdateListButtons();
    }

    private void UpdateListButtons()
    {
        ListCategory? category = SelectedCategory;
        bool selected = _listItems.SelectedItems.Count > 0;
        _listRemove.Enabled = selected && category is { ReadOnly: false };
        _listShortcut.Enabled = selected;
    }

    private void AddListItem()
    {
        if (SelectedCategory is not { ReadOnly: false } category)
            return;

        List<string> list = category.Get(_model);
        var added = new List<string>();
        foreach (string raw in _listInput.Text.Split(new[] { ',', ' ', '\n', '\r', '\t' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string value = category.Normalize(raw);
            if (value.Length == 0)
            {
                _statusLabel.Text = L.Format("config.error.invalidItem", raw);
                return;
            }
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase) &&
                !added.Contains(value, StringComparer.OrdinalIgnoreCase))
                added.Add(value);
        }

        list.AddRange(added);
        _model.Resolve();
        _listInput.Clear();
        _statusLabel.Text = "";
        ShowSelectedList();
    }

    private void RemoveSelectedListItems()
    {
        if (SelectedCategory is not { ReadOnly: false } category)
            return;
        List<string> list = category.Get(_model);
        foreach (string value in _listItems.SelectedItems.Cast<string>().ToArray())
            list.RemoveAll(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        _model.Resolve();
        ShowSelectedList();
    }

    private void CreateShortcuts()
    {
        var created = new List<string>();
        foreach (string url in _listItems.SelectedItems.Cast<string>())
        {
            if (WindowsIntegration.CreateDesktopShortcut(url, out string path, out string error))
                created.Add(Path.GetFileName(path));
            else
            {
                _statusLabel.Text = L.Format("config.error.shortcut", error);
                return;
            }
        }
        if (created.Count > 0)
        {
            MessageBox.Show(this, L.Format("config.shortcut.created", string.Join(Environment.NewLine, created)),
                "TabBouncer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void BrowseChrome()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = L.T("config.browse.filter"),
            CheckFileExists = true
        };
        try
        {
            string current = Environment.ExpandEnvironmentVariables(_chromePath.Text.Trim());
            if (current.Length > 0 && File.Exists(current))
                dialog.InitialDirectory = Path.GetDirectoryName(current);
        }
        catch
        {
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _chromePath.Text = dialog.FileName;
    }

    private void RestoreDefaults()
    {
        if (MessageBox.Show(this, L.T("config.defaults.confirm"), "TabBouncer",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _model = Config.Defaults();
        _model.Enabled = false;
        _autoRun.Checked = false;
        LoadModelIntoControls();
        _statusLabel.Text = L.T("config.defaults.pending");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S))
        {
            Save();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Save()
    {
        bool saved;
        string error;
        if (_tabs.SelectedTab == _jsonPage)
        {
            saved = Program.TrySaveConfigText(_editor.Text, out error);
            if (saved && Program.ParseConfig(_editor.Text, out _) is { } parsed)
                _model = parsed;
        }
        else
        {
            ReadControlsIntoModel();
            saved = Program.TrySaveConfig(_model, out error);
        }

        if (!saved)
        {
            _statusLabel.Text = L.Format("config.error.save", error);
            return;
        }

        if (_autoRun.Enabled && _autoRun.Checked != _originalAutoRun &&
            !WindowsIntegration.SetAutoRun(_autoRun.Checked, out string autoRunError))
        {
            _statusLabel.Text = L.Format("config.error.autoRun", autoRunError);
            return;
        }

        if (!_model.Language.Equals(_originalLanguage, StringComparison.Ordinal))
        {
            MessageBox.Show(this, L.T("config.language.restart"), "TabBouncer",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
