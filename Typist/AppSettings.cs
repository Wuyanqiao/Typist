using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typist;

/// <summary>
/// Manages application settings with JSON persistence.
/// Settings auto-save on property changes via Save() calls.
/// </summary>
public sealed class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Typist");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // --- Typing defaults ---
    public int DelayPerCharacterMs { get; set; } = 60;
    public int DelayVariationMs { get; set; } = 20;      // Random +/- jitter around base delay
    public int KeyPressDurationMs { get; set; } = 5;     // How long each key is held down
    public int StartDelaySeconds { get; set; } = 5;
    public bool UseEnterForLineBreaks { get; set; } = true;
    public bool MinimizeBeforeTyping { get; set; } = true;
    public bool AutoRestoreAfterTyping { get; set; } = true;

    // --- Target ---
    public int? SavedTargetX { get; set; }
    public int? SavedTargetY { get; set; }
    public int TargetCaptureDelaySeconds { get; set; } = 3;

    // --- UI state ---
    public int WindowWidth { get; set; } = 1020;
    public int WindowHeight { get; set; } = 750;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoSaveDraft { get; set; } = true;
    public int FontSizePt { get; set; } = 11;
    public string? LastUsedTemplateId { get; set; }

    // --- Draft slots ---
    public List<DraftSlot> DraftSlots { get; set; } = new()
    {
        new DraftSlot { Name = "草稿 1", Content = "" }
    };
    public int ActiveSlotIndex { get; set; } = 0;

    // --- Text templates (persisted separately but loaded into memory) ---
    public List<TextTemplate> Templates { get; set; } = new();

    // --- Typing history ---
    public List<TypingSessionRecord> RecentSessions { get; set; } = new();
    public int MaxRecentSessions { get; set; } = 50;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail - settings persistence is best-effort
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Fall through to defaults
        }
        return new AppSettings();
    }

    public string GetSettingsDir() => SettingsDir;
}

/// <summary>
/// A named text slot that persists between sessions.
/// </summary>
public sealed class DraftSlot
{
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// A reusable text template with metadata.
/// </summary>
public sealed class TextTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public string Category { get; set; } = "通用";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    public int UseCount { get; set; }
}

/// <summary>
/// Record of a completed typing session.
/// </summary>
public sealed class TypingSessionRecord
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int CharacterCount { get; set; }
    public int DelayMs { get; set; }
    public double DurationSeconds { get; set; }
    public string? PreviewText { get; set; }  // First 50 chars
    public bool Completed { get; set; }
}
