namespace Cueway.UI;

internal sealed class ThemePalette
{
    public required string Id { get; init; }
    public required bool IsLight { get; init; }
    public required uint BgWindow { get; init; }
    public required uint BgHeader { get; init; }
    public required uint BgCard { get; init; }
    public required uint BgCardElevated { get; init; }
    public required uint BgCardHover { get; init; }
    public required uint BgControl { get; init; }
    public required uint BgControlHover { get; init; }
    public required uint BgOsd { get; init; }
    public required uint BorderSubtle { get; init; }
    public required uint BorderStrong { get; init; }
    public required uint Accent { get; init; }
    public required uint AccentHover { get; init; }
    public required uint AccentSoft { get; init; }
    public required uint OnAccent { get; init; }
    public required uint Green { get; init; }
    public required uint GreenSoft { get; init; }
    public required uint Amber { get; init; }
    public required uint AmberSoft { get; init; }
    public required uint Red { get; init; }
    public required uint RedSoft { get; init; }
    public required uint TextPrimary { get; init; }
    public required uint TextSecondary { get; init; }
    public required uint TextMuted { get; init; }
    public required uint SwitchOffTrack { get; init; }
    public required uint SwitchKnob { get; init; }
    public required uint SwatchBg { get; init; }
    public required uint SwatchAccent { get; init; }
}

internal static class Theme
{
    public static readonly ThemePalette[] Presets =
    [
        // 1. Dark (Default)
        new ThemePalette
        {
            Id = "dark",
            IsLight = false,
            BgWindow = 0x111318,
            BgHeader = 0x161920,
            BgCard = 0x1B1F27,
            BgCardElevated = 0x202530,
            BgCardHover = 0x252B37,
            BgControl = 0x2A313E,
            BgControlHover = 0x364052,
            BgOsd = 0x171A21,
            BorderSubtle = 0x2B3240,
            BorderStrong = 0x3A4354,
            Accent = 0x4F9CF9,
            AccentHover = 0x6BB0FF,
            AccentSoft = 0x1E3352,
            OnAccent = 0xFFFFFF,
            Green = 0x34D399,
            GreenSoft = 0x15362D,
            Amber = 0xFBBF24,
            AmberSoft = 0x3D2E12,
            Red = 0xF87171,
            RedSoft = 0x3D1E23,
            TextPrimary = 0xF3F4F6,
            TextSecondary = 0x9CA3AF,
            TextMuted = 0x6B7280,
            SwitchOffTrack = 0x363E4D,
            SwitchKnob = 0xF9FAFB,
            SwatchBg = 0x1B1F27,
            SwatchAccent = 0x4F9CF9
        },
        // 2. Light (Crisp Fluent Daylight)
        new ThemePalette
        {
            Id = "light",
            IsLight = true,
            BgWindow = 0xF3F5F9,
            BgHeader = 0xFFFFFF,
            BgCard = 0xFFFFFF,
            BgCardElevated = 0xF8FAFC,
            BgCardHover = 0xEEF2F8,
            BgControl = 0xE5EAF2,
            BgControlHover = 0xD6DFEC,
            BgOsd = 0xFFFFFF,
            BorderSubtle = 0xDCE3EE,
            BorderStrong = 0xC2CCE0,
            Accent = 0x2563EB,
            AccentHover = 0x1D4ED8,
            AccentSoft = 0xDBEAFE,
            OnAccent = 0xFFFFFF,
            Green = 0x059669,
            GreenSoft = 0xD1FAE5,
            Amber = 0xD97706,
            AmberSoft = 0xFEF3C7,
            Red = 0xDC2626,
            RedSoft = 0xFEE2E2,
            TextPrimary = 0x111827,
            TextSecondary = 0x4B5563,
            TextMuted = 0x6B7280,
            SwitchOffTrack = 0xCBD5E1,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xF3F5F9,
            SwatchAccent = 0x2563EB
        },
        // 3. Nord (Arctic Slate & Frost Cyan)
        new ThemePalette
        {
            Id = "nord",
            IsLight = false,
            BgWindow = 0x1D222B,
            BgHeader = 0x242A35,
            BgCard = 0x2B3240,
            BgCardElevated = 0x313949,
            BgCardHover = 0x384153,
            BgControl = 0x3B4559,
            BgControlHover = 0x47536B,
            BgOsd = 0x242A35,
            BorderSubtle = 0x3E485B,
            BorderStrong = 0x4F5C73,
            Accent = 0x88C0D0,
            AccentHover = 0xA3D1DF,
            AccentSoft = 0x294452,
            OnAccent = 0x131920,
            Green = 0xA3BE8C,
            GreenSoft = 0x273B2D,
            Amber = 0xEBCB8B,
            AmberSoft = 0x443B27,
            Red = 0xBF616A,
            RedSoft = 0x45282D,
            TextPrimary = 0xECEFF4,
            TextSecondary = 0xB8C2D1,
            TextMuted = 0x8894A8,
            SwitchOffTrack = 0x434E63,
            SwitchKnob = 0xECEFF4,
            SwatchBg = 0x2B3240,
            SwatchAccent = 0x88C0D0
        },
        // 4. Emerald Forest
        new ThemePalette
        {
            Id = "emerald",
            IsLight = false,
            BgWindow = 0x0E1614,
            BgHeader = 0x131E1B,
            BgCard = 0x172521,
            BgCardElevated = 0x1C2D28,
            BgCardHover = 0x223630,
            BgControl = 0x263D36,
            BgControlHover = 0x314E45,
            BgOsd = 0x131E1B,
            BorderSubtle = 0x273C35,
            BorderStrong = 0x355249,
            Accent = 0x10B981,
            AccentHover = 0x34D399,
            AccentSoft = 0x123D31,
            OnAccent = 0xFFFFFF,
            Green = 0x34D399,
            GreenSoft = 0x133A2E,
            Amber = 0xFBBF24,
            AmberSoft = 0x3B2E13,
            Red = 0xF87171,
            RedSoft = 0x3D1F24,
            TextPrimary = 0xECFDF5,
            TextSecondary = 0x9CC2B4,
            TextMuted = 0x6A8F82,
            SwitchOffTrack = 0x2C463E,
            SwitchKnob = 0xECFDF5,
            SwatchBg = 0x172521,
            SwatchAccent = 0x10B981
        },
        // 5. Amethyst (Obsidian Plum & Lavender)
        new ThemePalette
        {
            Id = "amethyst",
            IsLight = false,
            BgWindow = 0x13111C,
            BgHeader = 0x191625,
            BgCard = 0x201C2F,
            BgCardElevated = 0x262238,
            BgCardHover = 0x2E2943,
            BgControl = 0x332D4B,
            BgControlHover = 0x41395E,
            BgOsd = 0x191625,
            BorderSubtle = 0x332D49,
            BorderStrong = 0x473F66,
            Accent = 0xA78BFA,
            AccentHover = 0xC4B5FD,
            AccentSoft = 0x2D2254,
            OnAccent = 0x141021,
            Green = 0x34D399,
            GreenSoft = 0x17372E,
            Amber = 0xFBBF24,
            AmberSoft = 0x3B2E17,
            Red = 0xFB7185,
            RedSoft = 0x421D29,
            TextPrimary = 0xF5F3FF,
            TextSecondary = 0xB8B0D4,
            TextMuted = 0x7E76A1,
            SwitchOffTrack = 0x3A3454,
            SwitchKnob = 0xF5F3FF,
            SwatchBg = 0x201C2F,
            SwatchAccent = 0xA78BFA
        },
        // 6. Warm Amber (Espresso & Gold)
        new ThemePalette
        {
            Id = "amber",
            IsLight = false,
            BgWindow = 0x161311,
            BgHeader = 0x1D1916,
            BgCard = 0x241F1B,
            BgCardElevated = 0x2B2520,
            BgCardHover = 0x342C26,
            BgControl = 0x3B322B,
            BgControlHover = 0x4B4037,
            BgOsd = 0x1D1916,
            BorderSubtle = 0x3A312A,
            BorderStrong = 0x52453B,
            Accent = 0xF59E0B,
            AccentHover = 0xFBBF24,
            AccentSoft = 0x452F10,
            OnAccent = 0x1A130A,
            Green = 0x34D399,
            GreenSoft = 0x17362A,
            Amber = 0xFBBF24,
            AmberSoft = 0x453214,
            Red = 0xF87171,
            RedSoft = 0x422022,
            TextPrimary = 0xFAF6F0,
            TextSecondary = 0xC2B4A6,
            TextMuted = 0x8A7C70,
            SwitchOffTrack = 0x443A32,
            SwitchKnob = 0xFAF6F0,
            SwatchBg = 0x241F1B,
            SwatchAccent = 0xF59E0B
        },
        // 7. Crimson (Onyx & Rose Red)
        new ThemePalette
        {
            Id = "crimson",
            IsLight = false,
            BgWindow = 0x141114,
            BgHeader = 0x1B161B,
            BgCard = 0x231C23,
            BgCardElevated = 0x2A222A,
            BgCardHover = 0x332933,
            BgControl = 0x3A2E3A,
            BgControlHover = 0x4A3B4A,
            BgOsd = 0x1B161B,
            BorderSubtle = 0x392C38,
            BorderStrong = 0x523E50,
            Accent = 0xF43F5E,
            AccentHover = 0xFB7185,
            AccentSoft = 0x4A1826,
            OnAccent = 0xFFFFFF,
            Green = 0x34D399,
            GreenSoft = 0x17372D,
            Amber = 0xFBBF24,
            AmberSoft = 0x3D2E15,
            Red = 0xFB7185,
            RedSoft = 0x4A1D28,
            TextPrimary = 0xFDF2F5,
            TextSecondary = 0xC8B3BC,
            TextMuted = 0x8C7680,
            SwitchOffTrack = 0x453745,
            SwitchKnob = 0xFDF2F5,
            SwatchBg = 0x231C23,
            SwatchAccent = 0xF43F5E
        },
        // 8. Warm Sand (Cozy Paper Light & Deep Teal)
        new ThemePalette
        {
            Id = "sand",
            IsLight = true,
            BgWindow = 0xF5EFE6,
            BgHeader = 0xFFFDF9,
            BgCard = 0xFFFDF9,
            BgCardElevated = 0xFAF5EC,
            BgCardHover = 0xEFE7DA,
            BgControl = 0xE8DEC9,
            BgControlHover = 0xDDD0B6,
            BgOsd = 0xFFFDF9,
            BorderSubtle = 0xE3D7C3,
            BorderStrong = 0xCBBAA0,
            Accent = 0x0F766E,
            AccentHover = 0x0D9488,
            AccentSoft = 0xCCFBF1,
            OnAccent = 0xFFFFFF,
            Green = 0x047857,
            GreenSoft = 0xD1FAE5,
            Amber = 0xB45309,
            AmberSoft = 0xFEF3C7,
            Red = 0xB91C1C,
            RedSoft = 0xFEE2E2,
            TextPrimary = 0x1F2421,
            TextSecondary = 0x4E544F,
            TextMuted = 0x737872,
            SwitchOffTrack = 0xC9B9A1,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xF5EFE6,
            SwatchAccent = 0x0F766E
        }
    ];

    private static volatile ThemePalette _current = Presets[0];

    public static ThemePalette Current => _current;
    public static string CurrentId => _current.Id;
    public static bool IsLight => _current.IsLight;

    public static uint BgWindow => _current.BgWindow;
    public static uint BgHeader => _current.BgHeader;
    public static uint BgCard => _current.BgCard;
    public static uint BgCardElevated => _current.BgCardElevated;
    public static uint BgCardHover => _current.BgCardHover;
    public static uint BgControl => _current.BgControl;
    public static uint BgControlHover => _current.BgControlHover;
    public static uint BgOsd => _current.BgOsd;

    public static uint BorderSubtle => _current.BorderSubtle;
    public static uint BorderStrong => _current.BorderStrong;

    public static uint Accent => _current.Accent;
    public static uint AccentHover => _current.AccentHover;
    public static uint AccentSoft => _current.AccentSoft;
    public static uint OnAccent => _current.OnAccent;

    public static uint Green => _current.Green;
    public static uint GreenSoft => _current.GreenSoft;

    public static uint Amber => _current.Amber;
    public static uint AmberSoft => _current.AmberSoft;

    public static uint Red => _current.Red;
    public static uint RedSoft => _current.RedSoft;

    public static uint TextPrimary => _current.TextPrimary;
    public static uint TextSecondary => _current.TextSecondary;
    public static uint TextMuted => _current.TextMuted;

    public static uint SwitchOffTrack => _current.SwitchOffTrack;
    public static uint SwitchKnob => _current.SwitchKnob;

    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "dark";
        string key = id.Trim().ToLowerInvariant();
        foreach (var p in Presets)
        {
            if (p.Id == key)
                return p.Id;
        }
        return "dark";
    }

    public static void SetTheme(string? id)
    {
        string normalized = NormalizeId(id);
        foreach (var p in Presets)
        {
            if (p.Id == normalized)
            {
                _current = p;
                return;
            }
        }
        _current = Presets[0];
    }

    public static int Scale(int px, uint dpi)
        => (int)Math.Round(px * (dpi / 96.0));

    public static float ScaleF(float px, uint dpi)
        => px * (dpi / 96f);

    public static uint ToColorRef(uint rgb)
    {
        uint r = (rgb >> 16) & 0xFF;
        uint g = (rgb >> 8) & 0xFF;
        uint b = rgb & 0xFF;
        return r | (g << 8) | (b << 16);
    }
}
