using System.Runtime.InteropServices;

namespace Typist;

/// <summary>
/// Represents a saved typing target with screen coordinates and metadata.
/// </summary>
public sealed class TypingTarget
{
    public Point ScreenPoint { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public string? Label { get; set; }

    public TypingTarget(Point screenPoint)
    {
        ScreenPoint = screenPoint;
    }

    public string DisplayText => Label is not null
        ? $"{Label} (X={ScreenPoint.X}, Y={ScreenPoint.Y})"
        : $"X={ScreenPoint.X}, Y={ScreenPoint.Y}";

    /// <summary>
    /// Checks if the target point is still on a visible screen.
    /// </summary>
    public bool IsOnScreen()
    {
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.Contains(ScreenPoint))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Handles target position capture and click simulation.
/// </summary>
public static class TargetSelector
{
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFAbsolute = 0x8000;
    private const uint MouseEventFMove = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    /// <summary>
    /// Captures the current mouse position as a typing target.
    /// </summary>
    public static TypingTarget CaptureCurrentMousePosition()
    {
        return new TypingTarget(Cursor.Position);
    }

    /// <summary>
    /// Clicks the target position to activate it, then waits for the window to be ready.
    /// Uses absolute mouse coordinates for multi-monitor support.
    /// </summary>
    public static async Task ClickTargetAsync(TypingTarget target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Move cursor to target
        Cursor.Position = target.ScreenPoint;
        await Task.Delay(100, cancellationToken);

        // Perform click
        mouse_event(MouseEventFLeftDown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(50, cancellationToken);
        mouse_event(MouseEventFLeftUp, 0, 0, 0, UIntPtr.Zero);

        // Wait for the target to gain focus
        await Task.Delay(200, cancellationToken);
    }

    /// <summary>
    /// Validates that a target is still usable (on a visible screen).
    /// Returns a reason string if invalid, null if valid.
    /// </summary>
    public static string? ValidateTarget(TypingTarget target)
    {
        if (!target.IsOnScreen())
            return "目标位置不在任何可见屏幕上。请重新选择目标。";
        return null;
    }
}
