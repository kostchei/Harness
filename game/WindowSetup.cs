using Godot;

/// <summary>
/// Optional startup window placement/autopin behavior.
/// Configure in project.godot:
/// - application/config/custom_window_target_screen (int, default -1 to disable)
/// - application/config/custom_window_always_on_top (bool, default false)
/// </summary>
public partial class WindowSetup : Node
{
    public override void _Ready()
    {
        var targetScreen = ReadInt("application/config/custom_window_target_screen", -1);
        var alwaysOnTop = ReadBool("application/config/custom_window_always_on_top", false);

        if (targetScreen >= 0 && targetScreen < DisplayServer.GetScreenCount())
        {
            DisplayServer.WindowSetCurrentScreen(targetScreen);
            var usable = DisplayServer.ScreenGetUsableRect(targetScreen);
            var winSize = DisplayServer.WindowGetSize();
            DisplayServer.WindowSetPosition(usable.Position + (usable.Size - winSize) / 2);
        }

        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, alwaysOnTop);
    }

    private static int ReadInt(string settingName, int fallback)
    {
        return ProjectSettings.HasSetting(settingName)
            ? (int)ProjectSettings.GetSetting(settingName)
            : fallback;
    }

    private static bool ReadBool(string settingName, bool fallback)
    {
        return ProjectSettings.HasSetting(settingName)
            ? (bool)ProjectSettings.GetSetting(settingName)
            : fallback;
    }
}
