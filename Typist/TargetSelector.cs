using System.Runtime.InteropServices;

namespace Typist;

public sealed record TypingTarget(Point ScreenPoint);

public static class TargetSelector
{
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;

    public static TypingTarget CaptureCurrentMousePosition()
    {
        return new TypingTarget(Cursor.Position);
    }

    public static async Task ClickTargetAsync(TypingTarget target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Cursor.Position = target.ScreenPoint;
        await Task.Delay(80, cancellationToken);
        mouse_event(MouseEventFLeftDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(40, cancellationToken);
        mouse_event(MouseEventFLeftUp, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(180, cancellationToken);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
