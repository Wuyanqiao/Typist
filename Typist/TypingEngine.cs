using System.Runtime.InteropServices;

namespace Typist;

/// <summary>
/// Options for a single typing session.
/// </summary>
public sealed record TypingOptions
{
    public int DelayPerCharacterMs { get; init; } = 60;
    public int DelayVariationMs { get; init; } = 20;
    public int KeyPressDurationMs { get; init; } = 5;
    public bool UseEnterForLineBreaks { get; init; } = true;
}

/// <summary>
/// Real-time progress report during typing.
/// </summary>
public sealed record TypingProgress
{
    public int SentCharacters { get; init; }
    public int TotalCharacters { get; init; }
    public double ElapsedSeconds { get; init; }
    public double EstimatedRemainingSeconds { get; init; }
    public string CurrentCharPreview { get; init; } = "";
}

/// <summary>
/// Core typing engine that simulates keyboard input via Win32 SendInput.
/// Supports variable speed with jitter for human-like typing, retry logic,
/// and configurable key press duration.
/// </summary>
public sealed class TypingEngine
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort VkReturn = 0x0D;
    private const ushort VkTab = 0x09;
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 50;

    private readonly Random _random = new();

    public async Task TypeAsync(
        string text,
        TypingOptions options,
        IProgress<TypingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var totalCharacters = normalizedText.Length;
        var startTime = DateTime.UtcNow;
        var avgDelayMs = Math.Max(options.DelayPerCharacterMs, 0);

        for (var i = 0; i < normalizedText.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var character = normalizedText[i];

            if (character == '\n' && options.UseEnterForLineBreaks)
            {
                SendVirtualKeyWithRetry(VkReturn, options.KeyPressDurationMs);
            }
            else if (character == '\t')
            {
                SendVirtualKeyWithRetry(VkTab, options.KeyPressDurationMs);
            }
            else
            {
                SendUnicodeCharacterWithRetry(character, options.KeyPressDurationMs);
            }

            // Calculate progress with time estimates
            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            var avgTimePerChar = i > 0 ? elapsed / i : avgDelayMs / 1000.0;
            var remaining = avgTimePerChar * (totalCharacters - i - 1);

            progress?.Report(new TypingProgress
            {
                SentCharacters = i + 1,
                TotalCharacters = totalCharacters,
                ElapsedSeconds = elapsed,
                EstimatedRemainingSeconds = remaining,
                CurrentCharPreview = character switch
                {
                    '\n' => "[Enter]",
                    '\t' => "[Tab]",
                    ' ' => "[Space]",
                    _ => character.ToString()
                }
            });

            // Variable delay with jitter for human-like feel
            if (avgDelayMs > 0 && i < normalizedText.Length - 1)
            {
                var jitter = options.DelayVariationMs > 0
                    ? _random.Next(-options.DelayVariationMs, options.DelayVariationMs + 1)
                    : 0;
                var actualDelay = Math.Max(0, avgDelayMs + jitter);
                await Task.Delay(actualDelay, cancellationToken);
            }
        }
    }

    private void SendUnicodeCharacterWithRetry(char character, int pressDurationMs)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                SendUnicodeCharacter(character, pressDurationMs);
                return;
            }
            catch (InvalidOperationException) when (attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    private void SendVirtualKeyWithRetry(ushort virtualKey, int pressDurationMs)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                SendVirtualKey(virtualKey, pressDurationMs);
                return;
            }
            catch (InvalidOperationException) when (attempt < MaxRetries)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    private static void SendUnicodeCharacter(char character, int pressDurationMs)
    {
        var inputs = new[]
        {
            CreateUnicodeInput(character, keyUp: false),
            CreateUnicodeInput(character, keyUp: true)
        };

        SendInputs(inputs);

        if (pressDurationMs > 0)
            Thread.Sleep(pressDurationMs);
    }

    private static void SendVirtualKey(ushort virtualKey, int pressDurationMs)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(virtualKey, keyUp: false),
            CreateVirtualKeyInput(virtualKey, keyUp: true)
        };

        SendInputs(inputs);

        if (pressDurationMs > 0)
            Thread.Sleep(pressDurationMs);
    }

    private static void SendInputs(Input[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput failed. Error code: {errorCode}. " +
                "The target application may require elevated privileges, or the current focus is not input-ready.");
        }
    }

    private static Input CreateUnicodeInput(char character, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                KeyboardInput = new KeyboardInput
                {
                    ScanCode = character,
                    Flags = KeyEventFUnicode | (keyUp ? KeyEventFKeyUp : 0)
                }
            }
        };
    }

    private static Input CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                KeyboardInput = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventFKeyUp : 0
                }
            }
        };
    }

    #region Native structs

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput MouseInput;
        [FieldOffset(0)] public KeyboardInput KeyboardInput;
        [FieldOffset(0)] public HardwareInput HardwareInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow, ParamHigh;
    }

    #endregion
}
