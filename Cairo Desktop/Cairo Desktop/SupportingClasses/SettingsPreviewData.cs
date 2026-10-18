namespace CairoDesktop.SupportingClasses
{
    /// <summary>
    /// Inert sample data used only by the settings XAML designer. Never constructs Settings or shell services.
    /// </summary>
    public class SettingsPreviewData
    {
        public string[] Options { get; } = { "Default", "Alternative" };
        public string[] Languages { get; } = { "English", "Français" };
        public string[] Themes { get; } = { "Default", "Cairo Dark" };

        public SettingsShortcutPreview[] Shortcuts { get; } =
        {
            new SettingsShortcutPreview { Key = "Enter", Command = "terminal", Value = "wt.exe" },
            new SettingsShortcutPreview { Key = "E", Command = "folder", Value = "C:\\Users", Ctrl = true },
            new SettingsShortcutPreview
            {
                Key = "E", Command = "folder", Value = "", Ctrl = true, IsLive = false,
                Status = "This key combination is already assigned to another shortcut."
            }
        };

        public BuiltInKeyRow[] BuiltInKeys { get; } =
        {
            new BuiltInKeyRow { Combo = "Win + Left", Action = "Focus window to the left", Status = "Active", IsLive = true },
            new BuiltInKeyRow { Combo = "Win + Shift + Right", Action = "Move window to the right", Status = "Active", IsLive = true },
            new BuiltInKeyRow { Combo = "Win + E", Action = "Open file manager", Status = "Replaced by your shortcut", IsLive = false }
        };
    }

    public class SettingsShortcutPreview
    {
        public string ModifierPrefix => "Win +";
        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public string Key { get; set; }
        public string Command { get; set; }
        public string Value { get; set; }
        public bool NeedsValue => true;
        public string ValueHint => "Command or path";
        public string Status { get; set; } = "Active";
        public bool IsLive { get; set; } = true;
        public string[] Keys { get; } = { "Enter", "E", "T" };
        public ShortcutAction[] Actions { get; } =
        {
            new ShortcutAction { Id = "terminal", Label = "Open terminal" },
            new ShortcutAction { Id = "folder", Label = "Open folder" }
        };
    }
}
