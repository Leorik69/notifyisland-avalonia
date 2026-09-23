namespace NotifyIsland;

/// <summary>Design tokens for the top-center overlay. No Apple assets.</summary>
public static class OverlayTokens
{
    public const string FillHex = "#080808";
    public const string TextHex = "#FFFFFF";
    public const string TextSecondaryHex = "#C8C8CC";
    public const string AccentHex = "#3D9CF0";
    public const string ErrorHex = "#E8A0A0";
    /// <summary>Collapsed capsule width (clock + optional unread dot).</summary>
    public const double CollapsedW = 140;
    /// <summary>Fixed capsule height for every kind — width-only morph.</summary>
    public const double CollapsedH = 30;
    public const double ExpandedMinW = 280;
    public const double ExpandedMaxW = 460;
    /// <summary>Soft-Out morph duration (ms).</summary>
    public const int MorphMs = 280;
    public const int DefaultNotifyMs = 4000;
}
