namespace Typist;

public partial class Form1 : Form
{
    private const int StartHotKeyId = 1001;
    private const int StopHotKeyId = 1002;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkS = 0x53;
    private const uint VkQ = 0x51;
    private const uint VkF11 = 0x7A;
    private const uint VkF12 = 0x7B;

    private readonly TypingEngine typingEngine = new();
    private CancellationTokenSource? typingCancellation;
    private TypingTarget? selectedTarget;

    private TextBox draftTextBox = null!;
    private NumericUpDown delayPerCharacterInput = null!;
    private NumericUpDown startDelayInput = null!;
    private CheckBox enterForLineBreaksCheckBox = null!;
    private CheckBox minimizeBeforeTypingCheckBox = null!;
    private Button startButton = null!;
    private Button stopButton = null!;
    private Button clearButton = null!;
    private Button selectTargetButton = null!;
    private Button clearTargetButton = null!;
    private ProgressBar typingProgressBar = null!;
    private Label statusLabel = null!;
    private Label targetStatusLabel = null!;
    private Label hotKeyStatusLabel = null!;

    public Form1()
    {
        InitializeComponent();
        BuildInterface();
        UpdateTypingState(isTyping: false);
    }

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

        hotKeyStatusLabel.Text = (startHotKey, stopHotKey) switch
        {
            ({ } start, { } stop) => $"热键：{start} 开始，{stop} 停止",
            (null, { } stop) => $"热键：{stop} 停止；开始热键被占用",
            ({ } start, null) => $"热键：{start} 开始；停止热键被占用",
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
            var hotKeyId = message.WParam.ToInt32();
            if (hotKeyId == StartHotKeyId)
            {
                _ = StartTypingAsync();
                return;
            }

            if (hotKeyId == StopHotKeyId)
            {
                StopTyping();
                return;
            }
        }

        base.WndProc(ref message);
    }

    private void BuildInterface()
    {
        Text = "Typist";
        MinimumSize = new Size(820, 620);
        Size = new Size(980, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16),
            BackColor = Color.FromArgb(248, 249, 251)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "打字助手",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        };

        draftTextBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Font = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.FixedSingle
        };

        var targetPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        targetPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        selectTargetButton = new Button
        {
            Text = "选择目标",
            Width = 110,
            Height = 34
        };
        selectTargetButton.Click += async (_, _) => await SelectTargetAsync();

        clearTargetButton = new Button
        {
            Text = "清除目标",
            Width = 110,
            Height = 34
        };
        clearTargetButton.Click += (_, _) => ClearTarget();

        targetStatusLabel = new Label
        {
            Text = "目标：未选择；开始时将输入到当前焦点位置",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(70, 76, 88),
            Margin = new Padding(12, 7, 0, 0)
        };

        targetPanel.Controls.Add(selectTargetButton, 0, 0);
        targetPanel.Controls.Add(clearTargetButton, 1, 0);
        targetPanel.Controls.Add(targetStatusLabel, 2, 0);

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 6,
            RowCount = 2,
            Margin = new Padding(0, 14, 0, 10)
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        delayPerCharacterInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 5000,
            Value = 60,
            Increment = 10,
            Dock = DockStyle.Fill
        };

        startDelayInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 60,
            Value = 5,
            Dock = DockStyle.Fill
        };

        enterForLineBreaksCheckBox = new CheckBox
        {
            Text = "换行使用 Enter",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 8, 20, 4)
        };

        minimizeBeforeTypingCheckBox = new CheckBox
        {
            Text = "开始后最小化窗口",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(0, 8, 20, 4)
        };

        startButton = new Button
        {
            Text = "开始",
            Width = 96,
            Height = 34
        };
        startButton.Click += async (_, _) => await StartTypingAsync();

        stopButton = new Button
        {
            Text = "停止",
            Width = 96,
            Height = 34
        };
        stopButton.Click += (_, _) => StopTyping();

        clearButton = new Button
        {
            Text = "清空",
            Width = 96,
            Height = 34
        };
        clearButton.Click += (_, _) => draftTextBox.Clear();

        AddLabeledControl(settings, "每字符间隔(ms)", delayPerCharacterInput, 0);
        AddLabeledControl(settings, "启动倒计时(秒)", startDelayInput, 2);
        settings.Controls.Add(enterForLineBreaksCheckBox, 0, 1);
        settings.SetColumnSpan(enterForLineBreaksCheckBox, 2);
        settings.Controls.Add(minimizeBeforeTypingCheckBox, 2, 1);
        settings.SetColumnSpan(minimizeBeforeTypingCheckBox, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(stopButton);
        buttons.Controls.Add(startButton);
        buttons.Controls.Add(clearButton);
        settings.Controls.Add(buttons, 5, 0);
        settings.SetRowSpan(buttons, 2);

        typingProgressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 18,
            Style = ProgressBarStyle.Continuous
        };

        statusLabel = new Label
        {
            Text = "就绪",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        hotKeyStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(90, 96, 108),
            Margin = new Padding(0, 4, 0, 0)
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(targetPanel, 0, 1);
        root.Controls.Add(draftTextBox, 0, 2);
        root.Controls.Add(settings, 0, 3);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3
        };
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.Controls.Add(typingProgressBar, 0, 0);
        footer.Controls.Add(statusLabel, 0, 1);
        footer.Controls.Add(hotKeyStatusLabel, 0, 2);

        root.Controls.Add(footer, 0, 4);
        Controls.Add(root);
    }

    private static void AddLabeledControl(TableLayoutPanel panel, string labelText, Control control, int column)
    {
        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0)
        }, column, 0);
        panel.Controls.Add(control, column + 1, 0);
    }

    private async Task StartTypingAsync()
    {
        if (typingCancellation is not null)
        {
            return;
        }

        var text = draftTextBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            statusLabel.Text = "草稿区为空";
            return;
        }

        typingCancellation = new CancellationTokenSource();
        var token = typingCancellation.Token;
        UpdateTypingState(isTyping: true);

        try
        {
            var startDelaySeconds = (int)startDelayInput.Value;
            if (minimizeBeforeTypingCheckBox.Checked)
            {
                WindowState = FormWindowState.Minimized;
            }

            for (var remaining = startDelaySeconds; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                statusLabel.Text = $"倒计时 {remaining} 秒";
                await Task.Delay(1000, token);
            }

            if (selectedTarget is not null)
            {
                statusLabel.Text = "正在激活目标位置";
                await TargetSelector.ClickTargetAsync(selectedTarget, token);
            }

            var options = new TypingOptions((int)delayPerCharacterInput.Value, enterForLineBreaksCheckBox.Checked);
            var progress = new Progress<TypingProgress>(typingProgress =>
            {
                typingProgressBar.Maximum = Math.Max(typingProgress.TotalCharacters, 1);
                typingProgressBar.Value = Math.Min(typingProgress.SentCharacters, typingProgressBar.Maximum);
                statusLabel.Text = $"输入中 {typingProgress.SentCharacters}/{typingProgress.TotalCharacters}";
            });

            await typingEngine.TypeAsync(text, options, progress, token);
            statusLabel.Text = "完成";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "已停止";
        }
        catch (Exception exception)
        {
            statusLabel.Text = exception.Message;
            WindowState = FormWindowState.Normal;
            Activate();
        }
        finally
        {
            typingCancellation.Dispose();
            typingCancellation = null;
            UpdateTypingState(isTyping: false);
        }
    }

    private void StopTyping()
    {
        typingCancellation?.Cancel();
    }

    private async Task SelectTargetAsync()
    {
        if (typingCancellation is not null)
        {
            return;
        }

        UpdateTargetSelectionState(isSelecting: true);
        var previousWindowState = WindowState;

        try
        {
            WindowState = FormWindowState.Minimized;

            for (var remaining = 3; remaining > 0; remaining--)
            {
                statusLabel.Text = $"把鼠标移到目标输入框上，{remaining} 秒后记录位置";
                await Task.Delay(1000);
            }

            selectedTarget = TargetSelector.CaptureCurrentMousePosition();
            UpdateTargetStatus();
            statusLabel.Text = "目标位置已记录";
        }
        finally
        {
            WindowState = previousWindowState == FormWindowState.Minimized
                ? FormWindowState.Normal
                : previousWindowState;
            Activate();
            UpdateTargetSelectionState(isSelecting: false);
        }
    }

    private void ClearTarget()
    {
        selectedTarget = null;
        UpdateTargetStatus();
        statusLabel.Text = "已清除目标位置";
    }

    private string? RegisterFirstAvailableHotKey(int id, params HotKeyCandidate[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (NativeMethods.RegisterHotKey(Handle, id, candidate.Modifiers, candidate.VirtualKey))
            {
                return candidate.DisplayText;
            }
        }

        return null;
    }

    private void UpdateTypingState(bool isTyping)
    {
        startButton.Enabled = !isTyping;
        clearButton.Enabled = !isTyping;
        selectTargetButton.Enabled = !isTyping;
        clearTargetButton.Enabled = !isTyping && selectedTarget is not null;
        draftTextBox.ReadOnly = isTyping;
        delayPerCharacterInput.Enabled = !isTyping;
        startDelayInput.Enabled = !isTyping;
        enterForLineBreaksCheckBox.Enabled = !isTyping;
        minimizeBeforeTypingCheckBox.Enabled = !isTyping;
        stopButton.Enabled = isTyping;

        if (!isTyping)
        {
            typingProgressBar.Value = 0;
        }
    }

    private void UpdateTargetSelectionState(bool isSelecting)
    {
        startButton.Enabled = !isSelecting;
        stopButton.Enabled = false;
        clearButton.Enabled = !isSelecting;
        selectTargetButton.Enabled = !isSelecting;
        clearTargetButton.Enabled = !isSelecting && selectedTarget is not null;
        draftTextBox.ReadOnly = isSelecting;
        delayPerCharacterInput.Enabled = !isSelecting;
        startDelayInput.Enabled = !isSelecting;
        enterForLineBreaksCheckBox.Enabled = !isSelecting;
        minimizeBeforeTypingCheckBox.Enabled = !isSelecting;
    }

    private void UpdateTargetStatus()
    {
        if (selectedTarget is null)
        {
            targetStatusLabel.Text = "目标：未选择；开始时将输入到当前焦点位置";
            clearTargetButton.Enabled = false;
            return;
        }

        targetStatusLabel.Text = $"目标：屏幕坐标 X={selectedTarget.ScreenPoint.X}, Y={selectedTarget.ScreenPoint.Y}";
        clearTargetButton.Enabled = typingCancellation is null;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
    }

    private sealed record HotKeyCandidate(string DisplayText, uint Modifiers, uint VirtualKey);
}
