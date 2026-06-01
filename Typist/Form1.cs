using System.Diagnostics;
using System.Text;

namespace Typist;

public partial class Form1 : Form
{
    #region Constants
    private const int StartHotKeyId = 1001;
    private const int StopHotKeyId = 1002;
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkS = 0x53;
    private const uint VkQ = 0x51;
    private const uint VkF11 = 0x7A;
    private const uint VkF12 = 0x7B;
    #endregion

    #region State
    private readonly TypingEngine _typingEngine = new();
    private readonly AppSettings _settings;
    private CancellationTokenSource? _typingCancellation;
    private TypingTarget? _selectedTarget;
    private DateTime _typingStartTime;
    private readonly System.Windows.Forms.Timer _autoSaveTimer;
    private bool _draftModified;
    #endregion

    #region UI Controls
    // Target area
    private Button _selectTargetButton = null!;
    private Button _clearTargetButton = null!;
    private Label _targetStatusLabel = null!;

    // Draft area
    private TabControl _draftTabControl = null!;
    private readonly List<TabPage> _draftTabs = new();
    private Button _addTabButton = null!;
    private Button _removeTabButton = null!;

    // Action bar under draft
    private Button _importFileButton = null!;
    private Button _pasteButton = null!;
    private Button _fontIncreaseButton = null!;
    private Button _fontDecreaseButton = null!;
    private Label _charCountLabel = null!;
    private Label _etaLabel = null!;

    // Speed presets
    private Button _speedSlowButton = null!;
    private Button _speedNormalButton = null!;
    private Button _speedFastButton = null!;
    private Button _speedInstantButton = null!;

    // Settings inputs
    private NumericUpDown _delayInput = null!;
    private NumericUpDown _variationInput = null!;
    private NumericUpDown _keyPressInput = null!;
    private NumericUpDown _countdownInput = null!;
    private CheckBox _enterLineBreakCheckBox = null!;
    private CheckBox _minimizeCheckBox = null!;
    private CheckBox _autoRestoreCheckBox = null!;
    private CheckBox _minimizeToTrayCheckBox = null!;

    // Templates
    private FlowLayoutPanel _templatePanel = null!;
    private Button _saveTemplateButton = null!;

    // Main controls
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _clearButton = null!;
    private ProgressBar _progressBar = null!;
    private Label _progressDetailLabel = null!;
    private Label _statusLabel = null!;
    private Label _hotKeyLabel = null!;
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    #endregion

    public Form1(AppSettings settings)
    {
        _settings = settings;
        _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _autoSaveTimer.Tick += (_, _) => AutoSaveDraft();

        InitializeComponent();
        BuildInterface();
        RestoreState();
        UpdateTypingState(isTyping: false);

        _autoSaveTimer.Start();
    }

    // Parameterless constructor for designer compatibility
    public Form1() : this(new AppSettings()) { }

    #region Hotkey Registration
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var startHotKey = RegisterFirstAvailableHotKey(
            StartHotKeyId,
            new HotKeyCandidate("Ctrl+Alt+S", ModControl | ModAlt, VkS),
            new HotKeyCandidate("Ctrl+Alt+F11", ModControl | ModAlt, VkF11));

        var stopHotKey = RegisterFirstAvailableHotKey(
            StopHotKeyId,
            new HotKeyCandidate("Ctrl+Alt+Q", ModControl | ModAlt, VkQ),
            new HotKeyCandidate("Ctrl+Alt+F12", ModControl | ModAlt, VkF12));

        _hotKeyLabel.Text = (startHotKey, stopHotKey) switch
        {
            ({ } s, { } q) => $"热键: {s} 开始 | {q} 停止",
            (null, { } q) => $"热键: {q} 停止 (开始热键被占用)",
            ({ } s, null) => $"热键: {s} 开始 (停止热键被占用)",
            _ => "热键被其他程序占用"
        };
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        NativeMethods.UnregisterHotKey(Handle, StartHotKeyId);
        NativeMethods.UnregisterHotKey(Handle, StopHotKeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotKey)
        {
            var id = message.WParam.ToInt32();
            if (id == StartHotKeyId) { _ = StartTypingAsync(); return; }
            if (id == StopHotKeyId) { StopTyping(); return; }
        }
        base.WndProc(ref message);
    }
    #endregion

    #region System Tray
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_settings.MinimizeToTray && WindowState == FormWindowState.Minimized)
        {
            Hide();
            _trayIcon.Visible = true;
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
    }
    #endregion

    #region Drag & Drop
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0 && (files[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                || files[0].EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || files[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                || files[0].EndsWith(".log", StringComparison.OrdinalIgnoreCase)))
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }
        }
        e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
        if (files?.Length > 0)
        {
            try
            {
                var text = File.ReadAllText(files[0], Encoding.UTF8);
                GetCurrentDraftTextBox().Text = text;
                _statusLabel.Text = $"已导入: {Path.GetFileName(files[0])}";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"导入失败: {ex.Message}";
            }
        }
    }
    #endregion

    #region UI Construction
    private void BuildInterface()
    {
        Text = "Typist - 打字助手";
        MinimumSize = new Size(880, 680);
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        // System tray
        BuildTrayIcon();

        // Root layout
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(14),
            BackColor = Color.FromArgb(245, 246, 248)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Title + target
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Draft tabs
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Draft action bar
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Speed presets
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Settings
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Templates
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Footer (progress, status)

        // Row 0: Header with title and target
        root.Controls.Add(BuildHeaderPanel(), 0, 0);

        // Row 1: Tabbed draft area
        root.Controls.Add(BuildDraftArea(), 0, 1);

        // Row 2: Draft action bar (import, paste, font, stats)
        root.Controls.Add(BuildDraftActionBar(), 0, 2);

        // Row 3: Speed presets
        root.Controls.Add(BuildSpeedPresets(), 0, 3);

        // Row 4: Settings row
        root.Controls.Add(BuildSettingsRow(), 0, 4);

        // Row 5: Templates
        root.Controls.Add(BuildTemplatePanel(), 0, 5);

        // Row 6: Footer
        root.Controls.Add(BuildFooter(), 0, 6);

        Controls.Add(root);
    }

    private Panel BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Title row
        var titleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 8)
        };
        titleRow.Controls.Add(new Label
        {
            Text = "Typist",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 44, 52),
            Margin = new Padding(0, 2, 12, 0)
        });
        titleRow.Controls.Add(new Label
        {
            Text = "打字助手",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 10F),
            ForeColor = Color.FromArgb(120, 126, 138),
            Margin = new Padding(0, 6, 0, 0)
        });

        // Target row
        var targetRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4)
        };

        _selectTargetButton = new Button { Text = "选择目标", Width = 100, Height = 32 };
        _selectTargetButton.Click += async (_, _) => await SelectTargetAsync();

        _clearTargetButton = new Button { Text = "清除", Width = 70, Height = 32, Enabled = false };
        _clearTargetButton.Click += (_, _) => ClearTarget();

        _targetStatusLabel = new Label
        {
            Text = "未选择目标 (将输入到当前焦点位置)",
            AutoSize = true,
            ForeColor = Color.FromArgb(80, 86, 98),
            Margin = new Padding(8, 8, 0, 0)
        };

        targetRow.Controls.Add(_selectTargetButton);
        targetRow.Controls.Add(_clearTargetButton);
        targetRow.Controls.Add(_targetStatusLabel);

        panel.Controls.Add(titleRow, 0, 0);
        panel.Controls.Add(targetRow, 0, 1);
        return panel;
    }

    private Control BuildDraftArea()
    {
        var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };

        // Tab control with add/remove buttons
        var tabRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1
        };

        _draftTabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(12, 6),
            Font = new Font(Font.FontFamily, 9F)
        };

        // Create tabs from saved slots
        for (var i = 0; i < _settings.DraftSlots.Count; i++)
        {
            var slot = _settings.DraftSlots[i];
            AddDraftTab(slot.Name, slot.Content);
        }

        if (_draftTabs.Count == 0)
            AddDraftTab("草稿 1", "");

        _draftTabControl.SelectedIndex = Math.Clamp(_settings.ActiveSlotIndex, 0, _draftTabs.Count - 1);
        _draftTabControl.SelectedIndexChanged += (_, _) => OnTabChanged();

        tabRow.Controls.Add(_draftTabControl, 0, 0);

        // Tab management buttons below
        var tabButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 0, 0)
        };
        _addTabButton = new Button { Text = "+ 新标签", Width = 80, Height = 26, Font = new Font(Font.FontFamily, 8F) };
        _addTabButton.Click += (_, _) => AddDraftTab($"草稿 {_draftTabs.Count + 1}", "");
        _removeTabButton = new Button { Text = "- 删除", Width = 70, Height = 26, Font = new Font(Font.FontFamily, 8F) };
        _removeTabButton.Click += (_, _) => RemoveCurrentTab();
        tabButtons.Controls.Add(_addTabButton);
        tabButtons.Controls.Add(_removeTabButton);

        var wrapper = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.Controls.Add(tabRow, 0, 0);
        wrapper.Controls.Add(tabButtons, 0, 1);
        container.Controls.Add(wrapper);
        return container;
    }

    private Control BuildDraftActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 0, 6),
            WrapContents = true
        };

        _importFileButton = new Button { Text = "导入文件", Width = 85, Height = 28, Font = new Font(Font.FontFamily, 8.5F) };
        _importFileButton.Click += (_, _) => ImportFile();

        _pasteButton = new Button { Text = "从剪贴板粘贴", Width = 105, Height = 28, Font = new Font(Font.FontFamily, 8.5F) };
        _pasteButton.Click += (_, _) => PasteFromClipboard();

        _fontDecreaseButton = new Button { Text = "A-", Width = 36, Height = 28, Font = new Font(Font.FontFamily, 8.5F) };
        _fontDecreaseButton.Click += (_, _) => ChangeFontSize(-1);

        _fontIncreaseButton = new Button { Text = "A+", Width = 36, Height = 28, Font = new Font(Font.FontFamily, 8.5F) };
        _fontIncreaseButton.Click += (_, _) => ChangeFontSize(1);

        _charCountLabel = new Label
        {
            Text = "字数: 0",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 96, 108),
            Margin = new Padding(14, 7, 0, 0),
            Font = new Font(Font.FontFamily, 8.5F)
        };

        _etaLabel = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 96, 108),
            Margin = new Padding(14, 7, 0, 0),
            Font = new Font(Font.FontFamily, 8.5F)
        };

        bar.Controls.Add(_importFileButton);
        bar.Controls.Add(_pasteButton);
        bar.Controls.Add(_fontDecreaseButton);
        bar.Controls.Add(_fontIncreaseButton);
        bar.Controls.Add(_charCountLabel);
        bar.Controls.Add(_etaLabel);
        return bar;
    }

    private Control BuildSpeedPresets()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 6)
        };

        panel.Controls.Add(new Label
        {
            Text = "速度预设:",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0),
            ForeColor = Color.FromArgb(70, 76, 88)
        });

        _speedSlowButton = CreatePresetButton("慢速 (100ms)", () => SetSpeed(100));
        _speedNormalButton = CreatePresetButton("正常 (60ms)", () => SetSpeed(60));
        _speedFastButton = CreatePresetButton("快速 (30ms)", () => SetSpeed(30));
        _speedInstantButton = CreatePresetButton("极速 (0ms)", () => SetSpeed(0));

        panel.Controls.Add(_speedSlowButton);
        panel.Controls.Add(_speedNormalButton);
        panel.Controls.Add(_speedFastButton);
        panel.Controls.Add(_speedInstantButton);

        HighlightActivePreset();
        return panel;
    }

    private Control BuildSettingsRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 8,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        _delayInput = new NumericUpDown { Minimum = 0, Maximum = 5000, Value = _settings.DelayPerCharacterMs, Increment = 10, Dock = DockStyle.Fill };
        _delayInput.ValueChanged += (_, _) => { _settings.DelayPerCharacterMs = (int)_delayInput.Value; UpdateEta(); HighlightActivePreset(); };

        _variationInput = new NumericUpDown { Minimum = 0, Maximum = 2000, Value = _settings.DelayVariationMs, Increment = 5, Dock = DockStyle.Fill };
        _variationInput.ValueChanged += (_, _) => _settings.DelayVariationMs = (int)_variationInput.Value;

        _keyPressInput = new NumericUpDown { Minimum = 0, Maximum = 200, Value = _settings.KeyPressDurationMs, Increment = 1, Dock = DockStyle.Fill };
        _keyPressInput.ValueChanged += (_, _) => _settings.KeyPressDurationMs = (int)_keyPressInput.Value;

        _countdownInput = new NumericUpDown { Minimum = 0, Maximum = 60, Value = _settings.StartDelaySeconds, Dock = DockStyle.Fill };
        _countdownInput.ValueChanged += (_, _) => _settings.StartDelaySeconds = (int)_countdownInput.Value;

        AddLabeledControl(panel, "字符间隔(ms)", _delayInput, 0);
        AddLabeledControl(panel, "随机抖动(ms)", _variationInput, 2);
        AddLabeledControl(panel, "按键时长(ms)", _keyPressInput, 4);
        AddLabeledControl(panel, "倒计时(秒)", _countdownInput, 6);

        // Checkboxes row
        _enterLineBreakCheckBox = new CheckBox { Text = "换行用 Enter", AutoSize = true, Checked = _settings.UseEnterForLineBreaks, Margin = new Padding(0, 4, 16, 2) };
        _enterLineBreakCheckBox.CheckedChanged += (_, _) => _settings.UseEnterForLineBreaks = _enterLineBreakCheckBox.Checked;

        _minimizeCheckBox = new CheckBox { Text = "开始后最小化", AutoSize = true, Checked = _settings.MinimizeBeforeTyping, Margin = new Padding(0, 4, 16, 2) };
        _minimizeCheckBox.CheckedChanged += (_, _) => _settings.MinimizeBeforeTyping = _minimizeCheckBox.Checked;

        _autoRestoreCheckBox = new CheckBox { Text = "完成后恢复窗口", AutoSize = true, Checked = _settings.AutoRestoreAfterTyping, Margin = new Padding(0, 4, 16, 2) };
        _autoRestoreCheckBox.CheckedChanged += (_, _) => _settings.AutoRestoreAfterTyping = _autoRestoreCheckBox.Checked;

        _minimizeToTrayCheckBox = new CheckBox { Text = "最小化到托盘", AutoSize = true, Checked = _settings.MinimizeToTray, Margin = new Padding(0, 4, 16, 2) };
        _minimizeToTrayCheckBox.CheckedChanged += (_, _) => _settings.MinimizeToTray = _minimizeToTrayCheckBox.Checked;

        panel.Controls.Add(_enterLineBreakCheckBox, 0, 1);
        panel.SetColumnSpan(_enterLineBreakCheckBox, 2);
        panel.Controls.Add(_minimizeCheckBox, 2, 1);
        panel.SetColumnSpan(_minimizeCheckBox, 2);
        panel.Controls.Add(_autoRestoreCheckBox, 4, 1);
        panel.SetColumnSpan(_autoRestoreCheckBox, 2);
        panel.Controls.Add(_minimizeToTrayCheckBox, 6, 1);

        return panel;
    }

    private Control BuildTemplatePanel()
    {
        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 6)
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4)
        };
        headerRow.Controls.Add(new Label
        {
            Text = "文本模板:",
            AutoSize = true,
            ForeColor = Color.FromArgb(70, 76, 88),
            Margin = new Padding(0, 4, 8, 0)
        });
        _saveTemplateButton = new Button { Text = "保存当前为模板", Width = 120, Height = 26, Font = new Font(Font.FontFamily, 8F) };
        _saveTemplateButton.Click += (_, _) => SaveCurrentAsTemplate();
        headerRow.Controls.Add(_saveTemplateButton);

        _templatePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0)
        };

        RefreshTemplateButtons();

        wrapper.Controls.Add(headerRow, 0, 0);
        wrapper.Controls.Add(_templatePanel, 0, 1);
        return wrapper;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Progress bar with detail
        var progressRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 20, Style = ProgressBarStyle.Continuous };
        _progressDetailLabel = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 96, 108),
            Margin = new Padding(10, 3, 0, 0),
            Font = new Font(Font.FontFamily, 8.5F)
        };
        progressRow.Controls.Add(_progressBar, 0, 0);
        progressRow.Controls.Add(_progressDetailLabel, 1, 0);

        // Action buttons row
        var actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        _stopButton = new Button { Text = "停止", Width = 90, Height = 34, Enabled = false };
        _stopButton.Click += (_, _) => StopTyping();

        _startButton = new Button { Text = "开始输入", Width = 100, Height = 34 };
        _startButton.Click += async (_, _) => await StartTypingAsync();

        _clearButton = new Button { Text = "清空", Width = 70, Height = 34 };
        _clearButton.Click += (_, _) => { GetCurrentDraftTextBox().Clear(); UpdateCharCount(); };

        buttonPanel.Controls.Add(_stopButton);
        buttonPanel.Controls.Add(_startButton);
        buttonPanel.Controls.Add(_clearButton);

        actionRow.Controls.Add(new Label { Text = "" }, 0, 0); // spacer
        actionRow.Controls.Add(buttonPanel, 1, 0);

        // Status + hotkey
        _statusLabel = new Label { Text = "就绪", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        _hotKeyLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(100, 106, 118),
            Margin = new Padding(0, 3, 0, 0)
        };

        footer.Controls.Add(progressRow, 0, 0);
        footer.SetColumnSpan(progressRow, 2);
        footer.Controls.Add(actionRow, 0, 1);
        footer.SetColumnSpan(actionRow, 2);
        footer.Controls.Add(_statusLabel, 0, 2);
        footer.Controls.Add(_hotKeyLabel, 1, 2);
        return footer;
    }

    private void BuildTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("显示窗口", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add("快速开始", null, async (_, _) => { RestoreFromTray(); await StartTypingAsync(); });
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add("退出", null, (_, _) => { _trayIcon.Visible = false; Application.Exit(); });

        _trayIcon = new NotifyIcon
        {
            Text = "Typist - 打字助手",
            ContextMenuStrip = _trayMenu,
            Visible = false
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        // Use a simple generated icon since we don't have an icon file
        _trayIcon.Icon = CreateDefaultIcon();
    }

    private static Icon CreateDefaultIcon()
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(66, 133, 244));
        g.DrawString("T", new Font("Arial", 10, FontStyle.Bold), Brushes.White, 1, 0);
        return Icon.FromHandle(bmp.GetHicon());
    }
    #endregion

    #region Draft Tab Management
    private void AddDraftTab(string name, string content)
    {
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Font = new Font("Consolas", _settings.FontSizePt, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.None,
            Text = content,
            AllowDrop = true
        };
        textBox.TextChanged += (_, _) => { _draftModified = true; UpdateCharCount(); };
        textBox.DragEnter += OnDragEnter;
        textBox.DragDrop += OnDragDrop;

        var tab = new TabPage(name) { Padding = new Padding(6) };
        tab.Controls.Add(textBox);
        _draftTabControl.TabPages.Add(tab);
        _draftTabs.Add(tab);
        _draftTabControl.SelectedTab = tab;

        UpdateCharCount();
    }

    private void RemoveCurrentTab()
    {
        if (_draftTabs.Count <= 1) return;
        var idx = _draftTabControl.SelectedIndex;
        _draftTabControl.TabPages.RemoveAt(idx);
        _draftTabs.RemoveAt(idx);
        if (_settings.DraftSlots.Count > idx)
            _settings.DraftSlots.RemoveAt(idx);
    }

    private void OnTabChanged()
    {
        _settings.ActiveSlotIndex = _draftTabControl.SelectedIndex;
        UpdateCharCount();
    }

    private TextBox GetCurrentDraftTextBox()
    {
        var tab = _draftTabControl.SelectedTab;
        if (tab?.Controls.Count > 0 && tab.Controls[0] is TextBox tb)
            return tb;
        return new TextBox();
    }

    private string GetCurrentDraftText() => GetCurrentDraftTextBox().Text;
    #endregion

    #region File & Clipboard
    private void ImportFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入文本文件",
            Filter = "文本文件 (*.txt;*.md;*.csv;*.log)|*.txt;*.md;*.csv;*.log|所有文件 (*.*)|*.*",
            FilterIndex = 0
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
                GetCurrentDraftTextBox().Text = text;
                _statusLabel.Text = $"已导入: {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"导入失败: {ex.Message}";
            }
        }
    }

    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsText())
        {
            GetCurrentDraftTextBox().Text = Clipboard.GetText();
            _statusLabel.Text = "已从剪贴板粘贴";
        }
        else
        {
            _statusLabel.Text = "剪贴板中没有文本";
        }
    }
    #endregion

    #region Font Size
    private void ChangeFontSize(int delta)
    {
        _settings.FontSizePt = Math.Clamp(_settings.FontSizePt + delta, 8, 24);
        foreach (var tab in _draftTabs)
        {
            if (tab.Controls.Count > 0 && tab.Controls[0] is TextBox tb)
                tb.Font = new Font("Consolas", _settings.FontSizePt);
        }
    }
    #endregion

    #region Speed Presets
    private Button CreatePresetButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Width = 100,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Font.FontFamily, 8.5F),
            Margin = new Padding(0, 0, 6, 0)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 212);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void SetSpeed(int delay)
    {
        _delayInput.Value = delay;
        _settings.DelayPerCharacterMs = delay;
        HighlightActivePreset();
        UpdateEta();
    }

    private void HighlightActivePreset()
{
    if (_delayInput is null) return;
        var current = (int)_delayInput.Value;
        SetPresetHighlight(_speedSlowButton, current == 100);
        SetPresetHighlight(_speedNormalButton, current == 60);
        SetPresetHighlight(_speedFastButton, current == 30);
        SetPresetHighlight(_speedInstantButton, current == 0);
    }

    private static void SetPresetHighlight(Button btn, bool active)
    {
        btn.BackColor = active ? Color.FromArgb(66, 133, 244) : SystemColors.Control;
        btn.ForeColor = active ? Color.White : SystemColors.ControlText;
        btn.FlatAppearance.BorderColor = active ? Color.FromArgb(66, 133, 244) : Color.FromArgb(200, 204, 212);
    }
    #endregion

    #region Templates
    private void RefreshTemplateButtons()
    {
        _templatePanel.Controls.Clear();

        foreach (var template in _settings.Templates.Take(8))
        {
            var btn = new Button
            {
                Text = template.Name,
                AutoSize = true,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 8F),
                Margin = new Padding(0, 0, 6, 0),
                Tag = template
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(180, 184, 192);
            btn.Click += (_, _) => LoadTemplate(template);

            // Right-click to delete
            btn.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (MessageBox.Show($"删除模板 \"{template.Name}\"?", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        _settings.Templates.Remove(template);
                        RefreshTemplateButtons();
                        _settings.Save();
                    }
                }
            };

            _templatePanel.Controls.Add(btn);
        }

        if (_settings.Templates.Count == 0)
        {
            _templatePanel.Controls.Add(new Label
            {
                Text = "暂无模板。输入文本后点击「保存当前为模板」创建。",
                AutoSize = true,
                ForeColor = Color.FromArgb(140, 146, 158),
                Margin = new Padding(0, 5, 0, 0),
                Font = new Font(Font.FontFamily, 8F)
            });
        }
    }

    private void SaveCurrentAsTemplate()
    {
        var text = GetCurrentDraftText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _statusLabel.Text = "草稿区为空，无法保存模板";
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "请输入模板名称:", "保存模板", $"模板 {_settings.Templates.Count + 1}");

        if (string.IsNullOrWhiteSpace(name)) return;

        var template = new TextTemplate
        {
            Name = name,
            Content = text,
            Category = "通用"
        };
        _settings.Templates.Add(template);
        RefreshTemplateButtons();
        _settings.Save();
        _statusLabel.Text = $"已保存模板: {name}";
    }

    private void LoadTemplate(TextTemplate template)
    {
        GetCurrentDraftTextBox().Text = template.Content;
        template.LastUsedAt = DateTime.Now;
        template.UseCount++;
        _statusLabel.Text = $"已加载模板: {template.Name}";
        _settings.LastUsedTemplateId = template.Id;
    }
    #endregion

    #region Char Count & ETA
    private void UpdateCharCount()
{
    if (_charCountLabel is null || _etaLabel is null) return;
        var text = GetCurrentDraftText();
        var count = text.Length;
        var lines = string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
        _charCountLabel.Text = $"字数: {count}  行数: {lines}";
        UpdateEta();
    }

    private void UpdateEta()
{
    if (_delayInput is null || _etaLabel is null) return;
        var text = GetCurrentDraftText();
        if (string.IsNullOrEmpty(text))
        {
            _etaLabel.Text = "";
            return;
        }
        var delay = (int)_delayInput.Value;
        if (delay <= 0)
        {
            _etaLabel.Text = "预计: <1秒";
            return;
        }
        var totalMs = (long)text.Length * delay;
        var seconds = totalMs / 1000.0;
        _etaLabel.Text = seconds switch
        {
            < 1 => "预计: <1秒",
            < 60 => $"预计: ~{seconds:F0}秒",
            < 3600 => $"预计: ~{seconds / 60:F1}分钟",
            _ => $"预计: ~{seconds / 3600:F1}小时"
        };
    }
    #endregion

    #region State Management
    private void RestoreState()
    {
        if (_settings.SavedTargetX.HasValue && _settings.SavedTargetY.HasValue)
        {
            _selectedTarget = new TypingTarget(new Point(_settings.SavedTargetX.Value, _settings.SavedTargetY.Value));
            _selectedTarget.Label = "已保存的目标";
            UpdateTargetStatus();
        }
    }

    private void AutoSaveDraft()
    {
        if (!_settings.AutoSaveDraft || !_draftModified) return;

        // Save all draft slots
        _settings.DraftSlots.Clear();
        foreach (var tab in _draftTabs)
        {
            if (tab.Controls.Count > 0 && tab.Controls[0] is TextBox tb)
            {
                _settings.DraftSlots.Add(new DraftSlot { Name = tab.Text, Content = tb.Text });
            }
        }
        _settings.ActiveSlotIndex = _draftTabControl.SelectedIndex;
        _settings.WindowWidth = Width;
        _settings.WindowHeight = Height;
        _settings.Save();
        _draftModified = false;
    }
    #endregion

    #region Target Management
    private async Task SelectTargetAsync()
    {
        if (_typingCancellation is not null) return;

        var previousWindowState = WindowState;
        _selectTargetButton.Enabled = false;
        _clearTargetButton.Enabled = false;

        try
        {
            WindowState = FormWindowState.Minimized;
            await Task.Delay(300);

            for (var remaining = _settings.TargetCaptureDelaySeconds; remaining > 0; remaining--)
            {
                _statusLabel.Text = $"移动鼠标到目标位置，{remaining}秒后记录...";
                await Task.Delay(1000);
            }

            _selectedTarget = TargetSelector.CaptureCurrentMousePosition();
            _selectedTarget.Label = "自选目标";

            // Save target to settings
            _settings.SavedTargetX = _selectedTarget.ScreenPoint.X;
            _settings.SavedTargetY = _selectedTarget.ScreenPoint.Y;

            UpdateTargetStatus();
            _statusLabel.Text = "目标已记录";
        }
        finally
        {
            WindowState = previousWindowState == FormWindowState.Minimized
                ? FormWindowState.Normal : previousWindowState;
            Activate();
            _selectTargetButton.Enabled = true;
        }
    }

    private void ClearTarget()
    {
        _selectedTarget = null;
        _settings.SavedTargetX = null;
        _settings.SavedTargetY = null;
        UpdateTargetStatus();
        _statusLabel.Text = "目标已清除";
    }

    private void UpdateTargetStatus()
    {
        if (_selectedTarget is null)
        {
            _targetStatusLabel.Text = "未选择目标 (将输入到当前焦点位置)";
            _clearTargetButton.Enabled = false;
            return;
        }
        _targetStatusLabel.Text = $"目标: {_selectedTarget.DisplayText}";
        _clearTargetButton.Enabled = _typingCancellation is null;
    }
    #endregion

    #region Typing Control
    private async Task StartTypingAsync()
    {
        if (_typingCancellation is not null) return;

        var text = GetCurrentDraftText();
        if (string.IsNullOrEmpty(text))
        {
            _statusLabel.Text = "草稿区为空";
            return;
        }

        // Validate target if set
        if (_selectedTarget is not null)
        {
            var issue = TargetSelector.ValidateTarget(_selectedTarget);
            if (issue is not null)
            {
                _statusLabel.Text = issue;
                return;
            }
        }

        _typingCancellation = new CancellationTokenSource();
        var token = _typingCancellation.Token;
        UpdateTypingState(isTyping: true);

        try
        {
            var countdown = (int)_countdownInput.Value;
            if (_minimizeCheckBox.Checked)
            {
                if (_settings.MinimizeToTray)
                {
                    WindowState = FormWindowState.Minimized;
                    Hide();
                    _trayIcon.Visible = true;
                }
                else
                {
                    WindowState = FormWindowState.Minimized;
                }
            }

            // Countdown
            for (var remaining = countdown; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                _statusLabel.Text = $"倒计时 {remaining}秒...";
                _trayIcon.Text = $"Typist - 倒计时 {remaining}秒";
                await Task.Delay(1000, token);
            }

            // Click target
            if (_selectedTarget is not null)
            {
                _statusLabel.Text = "正在激活目标...";
                _trayIcon.Text = "Typist - 激活目标";
                await TargetSelector.ClickTargetAsync(_selectedTarget, token);
            }

            // Start typing
            _typingStartTime = DateTime.UtcNow;
            var options = new TypingOptions
            {
                DelayPerCharacterMs = (int)_delayInput.Value,
                DelayVariationMs = (int)_variationInput.Value,
                KeyPressDurationMs = (int)_keyPressInput.Value,
                UseEnterForLineBreaks = _enterLineBreakCheckBox.Checked
            };

            var progress = new Progress<TypingProgress>(p =>
            {
                _progressBar.Maximum = Math.Max(p.TotalCharacters, 1);
                _progressBar.Value = Math.Min(p.SentCharacters, _progressBar.Maximum);
                _progressDetailLabel.Text = $"{p.SentCharacters}/{p.TotalCharacters}";
                if (p.EstimatedRemainingSeconds > 0)
                {
                    var eta = p.EstimatedRemainingSeconds switch
                    {
                        < 60 => $"{p.EstimatedRemainingSeconds:F0}秒",
                        < 3600 => $"{p.EstimatedRemainingSeconds / 60:F1}分",
                        _ => $"{p.EstimatedRemainingSeconds / 3600:F1}时"
                    };
                    _statusLabel.Text = $"输入中 {p.SentCharacters}/{p.TotalCharacters}  剩余 ~{eta}  当前: {p.CurrentCharPreview}";
                }
                else
                {
                    _statusLabel.Text = $"输入中 {p.SentCharacters}/{p.TotalCharacters}";
                }
                _trayIcon.Text = $"Typist - {p.SentCharacters}/{p.TotalCharacters}";
            });

            await _typingEngine.TypeAsync(text, options, progress, token);

            var elapsed = (DateTime.UtcNow - _typingStartTime).TotalSeconds;
            _statusLabel.Text = $"完成! 共 {text.Length} 字符，耗时 {elapsed:F1}秒";

            // Record session
            RecordSession(text, (int)_delayInput.Value, elapsed, completed: true);

            // Auto-restore window
            if (_settings.AutoRestoreAfterTyping && _settings.MinimizeToTray)
            {
                RestoreFromTray();
            }
            else if (_settings.AutoRestoreAfterTyping)
            {
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "已停止";
            RecordSession(text, (int)_delayInput.Value,
                (DateTime.UtcNow - _typingStartTime).TotalSeconds, completed: false);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"错误: {ex.Message}";
            WindowState = FormWindowState.Normal;
            Activate();
        }
        finally
        {
            _typingCancellation.Dispose();
            _typingCancellation = null;
            _progressDetailLabel.Text = "";
            _trayIcon.Text = "Typist - 打字助手";
            UpdateTypingState(isTyping: false);
        }
    }


    private void StopTyping() => _typingCancellation?.Cancel();

    private void RecordSession(string text, int delay, double duration, bool completed)
    {
        _settings.RecentSessions.Insert(0, new TypingSessionRecord
        {
            CharacterCount = text.Length,
            DelayMs = delay,
            DurationSeconds = duration,
            PreviewText = text.Length > 50 ? text[..50] + "..." : text,
            Completed = completed
        });

        while (_settings.RecentSessions.Count > _settings.MaxRecentSessions)
            _settings.RecentSessions.RemoveAt(_settings.RecentSessions.Count - 1);
    }
    #endregion

    #region UI State Updates
    private void UpdateTypingState(bool isTyping)
    {
        _startButton.Enabled = !isTyping;
        _clearButton.Enabled = !isTyping;
        _selectTargetButton.Enabled = !isTyping;
        _clearTargetButton.Enabled = !isTyping && _selectedTarget is not null;
        _importFileButton.Enabled = !isTyping;
        _pasteButton.Enabled = !isTyping;
        _saveTemplateButton.Enabled = !isTyping;
        _addTabButton.Enabled = !isTyping;
        _removeTabButton.Enabled = !isTyping;
        _delayInput.Enabled = !isTyping;
        _variationInput.Enabled = !isTyping;
        _keyPressInput.Enabled = !isTyping;
        _countdownInput.Enabled = !isTyping;
        _enterLineBreakCheckBox.Enabled = !isTyping;
        _minimizeCheckBox.Enabled = !isTyping;
        _autoRestoreCheckBox.Enabled = !isTyping;
        _minimizeToTrayCheckBox.Enabled = !isTyping;
        _speedSlowButton.Enabled = !isTyping;
        _speedNormalButton.Enabled = !isTyping;
        _speedFastButton.Enabled = !isTyping;
        _speedInstantButton.Enabled = !isTyping;
        GetCurrentDraftTextBox().ReadOnly = isTyping;
        _stopButton.Enabled = isTyping;

        if (!isTyping) _progressBar.Value = 0;
    }
    #endregion

    #region Helpers
    private static void AddLabeledControl(TableLayoutPanel panel, string label, Control control, int column)
    {
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0),
            Font = new Font("Microsoft YaHei UI", 8.5F)
        }, column, 0);
        panel.Controls.Add(control, column + 1, 0);
    }

    private string? RegisterFirstAvailableHotKey(int id, params HotKeyCandidate[] candidates)
    {
        foreach (var c in candidates)
        {
            if (NativeMethods.RegisterHotKey(Handle, id, c.Modifiers, c.VirtualKey))
                return c.DisplayText;
        }
        return null;
    }
    #endregion

    #region Native Methods
    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    private sealed record HotKeyCandidate(string DisplayText, uint Modifiers, uint VirtualKey);
    #endregion
}
