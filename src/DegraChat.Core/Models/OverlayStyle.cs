using System.Collections.Generic;

namespace DegraChat.Core.Models;

/// <summary>
/// Visual style profile for the OBS chat overlay.
/// </summary>
public class OverlayStyle
{
    public string ProfileName { get; set; } = "Default";

    // Background
    public string BackgroundColor { get; set; } = "#000000";
    public double BackgroundOpacity { get; set; } = 0.7;
    public double BorderRadius { get; set; } = 8.0;
    public double Padding { get; set; } = 12.0;

    // Message
    public string MessageFontFamily { get; set; } = "Inter";
    public double MessageFontSize { get; set; } = 16.0;
    public string MessageColor { get; set; } = "#FFFFFF";
    public double MessageLineHeight { get; set; } = 1.4;

    // Username
    public string UsernameFontFamily { get; set; } = "Inter";
    public double UsernameFontSize { get; set; } = 14.0;
    public bool UsernameBold { get; set; } = true;

    // Badges & Emotes
    public double BadgeSize { get; set; } = 18.0;
    public double EmoteSize { get; set; } = 28.0;

    // Layout
    public string MessageDirection { get; set; } = "bottom-up"; // "bottom-up" or "top-down"
    public int MaxMessages { get; set; } = 30;
    public double MessageSpacing { get; set; } = 4.0;

    // Animation
    public string AnimationIn { get; set; } = "fadeIn"; // "fadeIn", "slideIn", "none"
    public string AnimationOut { get; set; } = "fadeOut"; // "fadeOut", "slideOut", "none"
    public double AnimationDurationMs { get; set; } = 300.0;
    public double MessageDisplayTimeMs { get; set; } = 0; // 0 = no auto-hide

    // Separator
    public bool ShowSeparator { get; set; }
    public string SeparatorColor { get; set; } = "#333333";

    // Shadow
    public bool ShowShadow { get; set; }
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowBlur { get; set; } = 4.0;
    public double ShadowOffsetX { get; set; } = 2.0;
    public double ShadowOffsetY { get; set; } = 2.0;

    // Platform icon
    public bool ShowPlatformIcon { get; set; } = true;

    // Custom CSS (user can paste any overrides)
    public string CustomCss { get; set; } = string.Empty;
}
