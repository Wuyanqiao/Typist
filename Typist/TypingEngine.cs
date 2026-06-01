using System.Runtime.InteropServices;

namespace Typist;

public sealed record TypingOptions(int DelayPerCharacterMs, bool UseEnterForLineBreaks);

public sealed record TypingProgress(int SentCharacters, int TotalCharacters);

public sealed class TypingEngine
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const ushort VkReturn = 0x0D;

    public async Task TypeAsync(
        string text,
        TypingOptions options,
        IProgress<TypingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedText = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var totalCharacters = normalizedText.Length;

        for (var i = 0; i < normalizedText.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var character = normalizedText[i];
            if (character == '\n' && options.UseEnterForLineBreaks)
            {
                SendVirtualKey(VkReturn);
            }
            else
            {
                SendUnicodeCharacter(character);
            }

            progress?.Report(new TypingProgress(i + 1, totalCharacters));

            if (options.DelayPerCharacterMs > 0 && i < normalizedText.Length - 1)
            {
                await Task.Delay(options.DelayPerCharacterMs, cancellationToken);
            }
        }
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            CreateUnicodeInput(character, keyUp: false),
            CreateUnicodeInput(character, keyUp: true)
        };

        SendInputs(inputs);
    }

    private static void SendVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(virtualKey, keyUp: false),
            CreateVirtualKeyInput(virtualKey, keyUp: true)
        };

        SendInputs(inputs);
    }

    private static void SendInputs(Input[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Windows 没有接受完整输入序列。错误码：{errorCode}。目标程序可能以管理员权限运行，或当前焦点不可输入。");
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
        [FieldOffset(0)]
        public MouseInput MouseInput;

        [FieldOffset(0)]
        public KeyboardInput KeyboardInput;

        [FieldOffset(0)]
        public HardwareInput HardwareInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }
}
