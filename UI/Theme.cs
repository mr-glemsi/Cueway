using System.Globalization;
using Cueway.Config;

namespace Cueway.UI;

internal sealed class ThemePalette
{
    public required string Id { get; init; }
    public string? CustomName { get; init; }
    public bool IsCustom => CustomName != null;
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
        },
        // 9. Midnight OLED (Pitch Black & Electric Sky)
        new ThemePalette
        {
            Id = "midnight",
            IsLight = false,
            BgWindow = 0x080A0F,
            BgHeader = 0x0E1118,
            BgCard = 0x121620,
            BgCardElevated = 0x171C28,
            BgCardHover = 0x1D2433,
            BgControl = 0x21293A,
            BgControlHover = 0x2C374D,
            BgOsd = 0x0E1118,
            BorderSubtle = 0x202736,
            BorderStrong = 0x303B52,
            Accent = 0x38BDF8,
            AccentHover = 0x7DD3FC,
            AccentSoft = 0x103247,
            OnAccent = 0x081018,
            Green = 0x34D399,
            GreenSoft = 0x103329,
            Amber = 0xFBBF24,
            AmberSoft = 0x382A10,
            Red = 0xF87171,
            RedSoft = 0x3B1A20,
            TextPrimary = 0xF8FAFC,
            TextSecondary = 0x94A3B8,
            TextMuted = 0x64748B,
            SwitchOffTrack = 0x2B3447,
            SwitchKnob = 0xF8FAFC,
            SwatchBg = 0x080A0F,
            SwatchAccent = 0x38BDF8
        },
        // 10. Deep Ocean (Abyssal Navy & Luminous Teal)
        new ThemePalette
        {
            Id = "ocean",
            IsLight = false,
            BgWindow = 0x0B1420,
            BgHeader = 0x101C2C,
            BgCard = 0x152336,
            BgCardElevated = 0x1A2B42,
            BgCardHover = 0x213550,
            BgControl = 0x253C59,
            BgControlHover = 0x304D70,
            BgOsd = 0x101C2C,
            BorderSubtle = 0x22364F,
            BorderStrong = 0x324D6E,
            Accent = 0x22D3EE,
            AccentHover = 0x67E8F9,
            AccentSoft = 0x123C4A,
            OnAccent = 0x08151E,
            Green = 0x34D399,
            GreenSoft = 0x123A32,
            Amber = 0xFBBF24,
            AmberSoft = 0x3A2F17,
            Red = 0xFB7185,
            RedSoft = 0x3E1E2B,
            TextPrimary = 0xF0F9FF,
            TextSecondary = 0x9EC0D9,
            TextMuted = 0x688AA6,
            SwitchOffTrack = 0x2D4561,
            SwitchKnob = 0xF0F9FF,
            SwatchBg = 0x152336,
            SwatchAccent = 0x22D3EE
        },
        // 11. Sakura (Charcoal Plum & Cherry Blossom Pink)
        new ThemePalette
        {
            Id = "sakura",
            IsLight = false,
            BgWindow = 0x18131A,
            BgHeader = 0x201923,
            BgCard = 0x281F2C,
            BgCardElevated = 0x302536,
            BgCardHover = 0x3A2D41,
            BgControl = 0x413349,
            BgControlHover = 0x52405C,
            BgOsd = 0x201923,
            BorderSubtle = 0x3F3047,
            BorderStrong = 0x594464,
            Accent = 0xF472B6,
            AccentHover = 0xF9A8D4,
            AccentSoft = 0x4A1E3B,
            OnAccent = 0x1B0F17,
            Green = 0x34D399,
            GreenSoft = 0x18382F,
            Amber = 0xFBBF24,
            AmberSoft = 0x3F3017,
            Red = 0xFB7185,
            RedSoft = 0x481E2B,
            TextPrimary = 0xFDF4F9,
            TextSecondary = 0xCCB3C4,
            TextMuted = 0x907788,
            SwitchOffTrack = 0x4B3B54,
            SwitchKnob = 0xFDF4F9,
            SwatchBg = 0x281F2C,
            SwatchAccent = 0xF472B6
        },
        // 12. Frost Slate (Crisp Cool Light & Deep Indigo)
        new ThemePalette
        {
            Id = "slate",
            IsLight = true,
            BgWindow = 0xEEF2F7,
            BgHeader = 0xF8FAFC,
            BgCard = 0xFFFFFF,
            BgCardElevated = 0xF8FAFC,
            BgCardHover = 0xE9EEF6,
            BgControl = 0xE2E8F0,
            BgControlHover = 0xCBD5E1,
            BgOsd = 0xFFFFFF,
            BorderSubtle = 0xD5DEE9,
            BorderStrong = 0xB8C5D6,
            Accent = 0x4F46E5,
            AccentHover = 0x4338CA,
            AccentSoft = 0xE0E7FF,
            OnAccent = 0xFFFFFF,
            Green = 0x059669,
            GreenSoft = 0xD1FAE5,
            Amber = 0xD97706,
            AmberSoft = 0xFEF3C7,
            Red = 0xDC2626,
            RedSoft = 0xFEE2E2,
            TextPrimary = 0x0F172A,
            TextSecondary = 0x475569,
            TextMuted = 0x64748B,
            SwitchOffTrack = 0xCBD5E1,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xEEF2F7,
            SwatchAccent = 0x4F46E5
        }
    ];

    private static volatile ThemePalette _current = Presets[0];
    private static volatile ThemePalette[] _customPalettes = [];
    private static volatile ThemePalette? _previewPalette;

    public static ThemePalette Current => _previewPalette ?? _current;
    public static string CurrentId => _current.Id;
    public static IReadOnlyList<ThemePalette> CustomPalettes => _customPalettes;
    public static bool IsLight => Current.IsLight;

    public static uint BgWindow => Current.BgWindow;
    public static uint BgHeader => Current.BgHeader;
    public static uint BgCard => Current.BgCard;
    public static uint BgCardElevated => Current.BgCardElevated;
    public static uint BgCardHover => Current.BgCardHover;
    public static uint BgControl => Current.BgControl;
    public static uint BgControlHover => Current.BgControlHover;
    public static uint BgOsd => Current.BgOsd;

    public static uint BorderSubtle => Current.BorderSubtle;
    public static uint BorderStrong => Current.BorderStrong;

    public static uint Accent => Current.Accent;
    public static uint AccentHover => Current.AccentHover;
    public static uint AccentSoft => Current.AccentSoft;
    public static uint OnAccent => Current.OnAccent;

    public static uint Green => Current.Green;
    public static uint GreenSoft => Current.GreenSoft;

    public static uint Amber => Current.Amber;
    public static uint AmberSoft => Current.AmberSoft;

    public static uint Red => Current.Red;
    public static uint RedSoft => Current.RedSoft;

    public static uint TextPrimary => Current.TextPrimary;
    public static uint TextSecondary => Current.TextSecondary;
    public static uint TextMuted => Current.TextMuted;

    public static uint SwitchOffTrack => Current.SwitchOffTrack;
    public static uint SwitchKnob => Current.SwitchKnob;

    public static ThemePalette? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        string key = id.Trim();
        foreach (var p in Presets)
        {
            if (string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        foreach (var c in _customPalettes)
        {
            if (string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    public static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "dark";
        var found = FindById(id);
        return found != null ? found.Id : "dark";
    }

    public static void SetTheme(string? id)
    {
        string normalized = NormalizeId(id);
        _current = FindById(normalized) ?? Presets[0];
    }

    public static void SetCustomThemes(IReadOnlyList<CustomThemePreset>? presets)
    {
        if (presets == null || presets.Count == 0)
        {
            _customPalettes = [];
            return;
        }

        var list = new List<ThemePalette>(presets.Count);
        foreach (var p in presets)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) continue;
            uint bg = TryParseHex(p.BgHex, out uint b) ? b : 0x13161Fu;
            uint card = TryParseHex(p.CardHex, out uint c) ? c : SuggestCardColor(bg);
            uint accent = TryParseHex(p.AccentHex, out uint a) ? a : 0x6366F1u;
            string name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name.Trim();
            list.Add(GenerateCustomPalette(p.Id.Trim(), name, bg, card, accent));
        }
        _customPalettes = [.. list];

        // Refresh _current if the currently active theme is a custom theme that was just edited
        var refreshed = FindById(_current.Id);
        if (refreshed != null)
            _current = refreshed;
    }

    public static void SetPreviewPalette(ThemePalette? palette)
    {
        _previewPalette = palette;
    }

    public static uint BlendRgb(uint baseRgb, uint tintRgb, float amount)
    {
        float t = Math.Clamp(amount, 0f, 1f);
        int r1 = (int)((baseRgb >> 16) & 0xFF);
        int g1 = (int)((baseRgb >> 8) & 0xFF);
        int b1 = (int)(baseRgb & 0xFF);
        int r2 = (int)((tintRgb >> 16) & 0xFF);
        int g2 = (int)((tintRgb >> 8) & 0xFF);
        int b2 = (int)(tintRgb & 0xFF);
        uint r = (uint)Math.Clamp((int)MathF.Round(r1 + (r2 - r1) * t), 0, 255);
        uint g = (uint)Math.Clamp((int)MathF.Round(g1 + (g2 - g1) * t), 0, 255);
        uint b = (uint)Math.Clamp((int)MathF.Round(b1 + (b2 - b1) * t), 0, 255);
        return (r << 16) | (g << 8) | b;
    }

    public static float RelativeLuminance(uint rgb)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f;
        float g = ((rgb >> 8) & 0xFF) / 255f;
        float b = (rgb & 0xFF) / 255f;
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    public static uint SuggestCardColor(uint bgRgb)
    {
        bool isLight = RelativeLuminance(bgRgb) >= 0.52f;
        return isLight
            ? BlendRgb(bgRgb, 0xFFFFFF, 0.72f)
            : BlendRgb(bgRgb, 0xFFFFFF, 0.07f);
    }

    public static string FormatHex(uint rgb)
        => $"#{(rgb & 0xFFFFFF):X6}";

    public static bool TryParseHex(string? hex, out uint rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        string s = hex.Trim();
        if (s.StartsWith('#'))
            s = s[1..];
        if (s.Length != 6)
            return false;
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    public static ThemePalette GenerateCustomPalette(string id, string name, uint bgRgb, uint cardRgb, uint accentRgb)
    {
        bgRgb &= 0xFFFFFF;
        cardRgb &= 0xFFFFFF;
        accentRgb &= 0xFFFFFF;

        bool isLight = RelativeLuminance(bgRgb) >= 0.52f;
        float accentLum = RelativeLuminance(accentRgb);

        uint contrastTarget = isLight ? 0x0F172Au : 0xFFFFFFu;
        uint softBase = isLight ? 0xFFFFFF : cardRgb;

        uint bgHeader = isLight ? BlendRgb(bgRgb, 0xFFFFFF, 0.78f) : BlendRgb(bgRgb, cardRgb, 0.50f);
        uint bgElevated = isLight ? BlendRgb(cardRgb, bgRgb, 0.26f) : BlendRgb(cardRgb, 0xFFFFFF, 0.055f);
        uint bgHover = isLight ? BlendRgb(cardRgb, accentRgb, 0.08f) : BlendRgb(cardRgb, 0xFFFFFF, 0.11f);
        uint bgControl = isLight ? BlendRgb(bgRgb, 0x0F172A, 0.08f) : BlendRgb(cardRgb, 0xFFFFFF, 0.14f);
        uint bgControlHover = isLight ? BlendRgb(bgRgb, 0x0F172A, 0.16f) : BlendRgb(cardRgb, 0xFFFFFF, 0.22f);

        uint borderSubtle = isLight ? BlendRgb(bgRgb, 0x0F172A, 0.14f) : BlendRgb(cardRgb, 0xFFFFFF, 0.13f);
        uint borderStrong = isLight ? BlendRgb(bgRgb, 0x0F172A, 0.26f) : BlendRgb(cardRgb, 0xFFFFFF, 0.24f);

        uint accentHover = accentLum > 0.65f
            ? BlendRgb(accentRgb, 0x000000, 0.14f)
            : BlendRgb(accentRgb, 0xFFFFFF, 0.20f);
        uint accentSoft = isLight
            ? BlendRgb(softBase, accentRgb, 0.16f)
            : BlendRgb(cardRgb, accentRgb, 0.24f);
        uint onAccent = accentLum >= 0.56f ? 0x111827u : 0xFFFFFFu;

        uint green = isLight ? 0x059669u : 0x34D399u;
        uint amber = isLight ? 0xD97706u : 0xFBBF24u;
        uint red = isLight ? 0xDC2626u : 0xF87171u;

        return new ThemePalette
        {
            Id = id,
            CustomName = name,
            IsLight = isLight,
            BgWindow = bgRgb,
            BgHeader = bgHeader,
            BgCard = cardRgb,
            BgCardElevated = bgElevated,
            BgCardHover = bgHover,
            BgControl = bgControl,
            BgControlHover = bgControlHover,
            BgOsd = bgHeader,
            BorderSubtle = borderSubtle,
            BorderStrong = borderStrong,
            Accent = accentRgb,
            AccentHover = accentHover,
            AccentSoft = accentSoft,
            OnAccent = onAccent,
            Green = green,
            GreenSoft = BlendRgb(softBase, green, isLight ? 0.16f : 0.20f),
            Amber = amber,
            AmberSoft = BlendRgb(softBase, amber, isLight ? 0.16f : 0.20f),
            Red = red,
            RedSoft = BlendRgb(softBase, red, isLight ? 0.16f : 0.20f),
            TextPrimary = isLight ? BlendRgb(0x0F172A, cardRgb, 0.05f) : BlendRgb(0xF8FAFC, cardRgb, 0.05f),
            TextSecondary = isLight ? BlendRgb(bgRgb, contrastTarget, 0.66f) : BlendRgb(cardRgb, contrastTarget, 0.62f),
            TextMuted = isLight ? BlendRgb(bgRgb, contrastTarget, 0.48f) : BlendRgb(cardRgb, contrastTarget, 0.42f),
            SwitchOffTrack = isLight ? BlendRgb(bgRgb, 0x0F172A, 0.20f) : BlendRgb(cardRgb, 0xFFFFFF, 0.22f),
            SwitchKnob = 0xFFFFFFu,
            SwatchBg = bgRgb,
            SwatchAccent = accentRgb
        };
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
