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
        // 1. Dark (Default — Refined Obsidian Aura)
        new ThemePalette
        {
            Id = "dark",
            IsLight = false,
            BgWindow = 0x101217,
            BgHeader = 0x14171F,
            BgCard = 0x181C25,
            BgCardElevated = 0x1E232F,
            BgCardHover = 0x242A38,
            BgControl = 0x282F3E,
            BgControlHover = 0x333C4F,
            BgOsd = 0x151820,
            BorderSubtle = 0x252B38,
            BorderStrong = 0x353E50,
            Accent = 0x5B9CF6,
            AccentHover = 0x7AB0FA,
            AccentSoft = 0x1B2B45,
            OnAccent = 0x0B1018,
            Green = 0x4ADE80,
            GreenSoft = 0x142E23,
            Amber = 0xF5B951,
            AmberSoft = 0x342814,
            Red = 0xF2727D,
            RedSoft = 0x381D24,
            TextPrimary = 0xEEF1F6,
            TextSecondary = 0x9AA3B5,
            TextMuted = 0x697286,
            SwitchOffTrack = 0x313847,
            SwitchKnob = 0xF3F5F9,
            SwatchBg = 0x181C25,
            SwatchAccent = 0x5B9CF6
        },
        // 2. AMOLED Black (Pure OLED & Warm Studio Tangerine)
        new ThemePalette
        {
            Id = "midnight",
            IsLight = false,
            BgWindow = 0x000000,
            BgHeader = 0x08090C,
            BgCard = 0x0D0E12,
            BgCardElevated = 0x14161C,
            BgCardHover = 0x1B1E26,
            BgControl = 0x1E212A,
            BgControlHover = 0x2A2E3A,
            BgOsd = 0x0B0C10,
            BorderSubtle = 0x1C1F27,
            BorderStrong = 0x2E3240,
            Accent = 0xF28C52,
            AccentHover = 0xF7A474,
            AccentSoft = 0x331E12,
            OnAccent = 0x140C07,
            Green = 0x4CC990,
            GreenSoft = 0x102A1F,
            Amber = 0xF2B857,
            AmberSoft = 0x312410,
            Red = 0xED6A6A,
            RedSoft = 0x331618,
            TextPrimary = 0xF4F5F7,
            TextSecondary = 0x9A9FA8,
            TextMuted = 0x626773,
            SwitchOffTrack = 0x262933,
            SwitchKnob = 0xF4F5F7,
            SwatchBg = 0x000000,
            SwatchAccent = 0xF28C52
        },
        // 3. Gruvbox Material (Dark Medium)
        new ThemePalette
        {
            Id = "gruvbox_material",
            IsLight = false,
            BgWindow = 0x1D2021,
            BgHeader = 0x242424,
            BgCard = 0x282828,
            BgCardElevated = 0x302E2D,
            BgCardHover = 0x383533,
            BgControl = 0x3C3836,
            BgControlHover = 0x4B4542,
            BgOsd = 0x252423,
            BorderSubtle = 0x3A3634,
            BorderStrong = 0x504945,
            Accent = 0xE78A4E,
            AccentHover = 0xF0A06C,
            AccentSoft = 0x3E2B1E,
            OnAccent = 0x1D1815,
            Green = 0xA9B665,
            GreenSoft = 0x2F3421,
            Amber = 0xD8A657,
            AmberSoft = 0x3D311E,
            Red = 0xEA6962,
            RedSoft = 0x402322,
            TextPrimary = 0xD4BE98,
            TextSecondary = 0xA89984,
            TextMuted = 0x7C6F64,
            SwitchOffTrack = 0x45403D,
            SwitchKnob = 0xE2CCA9,
            SwatchBg = 0x282828,
            SwatchAccent = 0xE78A4E
        },
        // 4. Gruvbox Dark Soft
        new ThemePalette
        {
            Id = "gruvbox_soft",
            IsLight = false,
            BgWindow = 0x282828,
            BgHeader = 0x2E2C2B,
            BgCard = 0x32302F,
            BgCardElevated = 0x3A3735,
            BgCardHover = 0x433F3C,
            BgControl = 0x46413E,
            BgControlHover = 0x57504B,
            BgOsd = 0x2F2D2C,
            BorderSubtle = 0x45403C,
            BorderStrong = 0x5B534D,
            Accent = 0xFABD2F,
            AccentHover = 0xFCCD5C,
            AccentSoft = 0x473B1C,
            OnAccent = 0x221E17,
            Green = 0xB8BB26,
            GreenSoft = 0x393B1D,
            Amber = 0xFE8019,
            AmberSoft = 0x4A2E19,
            Red = 0xFB4934,
            RedSoft = 0x4A2421,
            TextPrimary = 0xEBDBB2,
            TextSecondary = 0xBDAE93,
            TextMuted = 0x928374,
            SwitchOffTrack = 0x504945,
            SwitchKnob = 0xFBF1C7,
            SwatchBg = 0x32302F,
            SwatchAccent = 0xFABD2F
        },
        // 5. Dracula (Refined Dark Plum)
        new ThemePalette
        {
            Id = "dracula",
            IsLight = false,
            BgWindow = 0x1E1F29,
            BgHeader = 0x242632,
            BgCard = 0x282A36,
            BgCardElevated = 0x303342,
            BgCardHover = 0x383C4E,
            BgControl = 0x3D4154,
            BgControlHover = 0x4B5068,
            BgOsd = 0x242632,
            BorderSubtle = 0x3A3E52,
            BorderStrong = 0x4E546E,
            Accent = 0xBD93F9,
            AccentHover = 0xD0B0FB,
            AccentSoft = 0x362C4D,
            OnAccent = 0x191624,
            Green = 0x50FA7B,
            GreenSoft = 0x1F3E30,
            Amber = 0xFFB86C,
            AmberSoft = 0x443528,
            Red = 0xFF6E6E,
            RedSoft = 0x45262E,
            TextPrimary = 0xF4F4F0,
            TextSecondary = 0xB4B8D0,
            TextMuted = 0x7A80A0,
            SwitchOffTrack = 0x44475A,
            SwitchKnob = 0xF8F8F2,
            SwatchBg = 0x282A36,
            SwatchAccent = 0xBD93F9
        },
        // 6. Nord (Authentic Arctic Slate)
        new ThemePalette
        {
            Id = "nord",
            IsLight = false,
            BgWindow = 0x21262F,
            BgHeader = 0x272D38,
            BgCard = 0x2E3440,
            BgCardElevated = 0x353C4A,
            BgCardHover = 0x3D4555,
            BgControl = 0x40495A,
            BgControlHover = 0x4C566A,
            BgOsd = 0x272D38,
            BorderSubtle = 0x3D4556,
            BorderStrong = 0x4E586D,
            Accent = 0x88C0D0,
            AccentHover = 0xA2D1DE,
            AccentSoft = 0x2C424F,
            OnAccent = 0x182028,
            Green = 0xA3BE8C,
            GreenSoft = 0x2D3C31,
            Amber = 0xEBCB8B,
            AmberSoft = 0x453E2C,
            Red = 0xBF616A,
            RedSoft = 0x432B32,
            TextPrimary = 0xECEFF4,
            TextSecondary = 0xB8C2D1,
            TextMuted = 0x818DA3,
            SwitchOffTrack = 0x434C5E,
            SwitchKnob = 0xECEFF4,
            SwatchBg = 0x2E3440,
            SwatchAccent = 0x88C0D0
        },
        // 7. Catppuccin Mocha
        new ThemePalette
        {
            Id = "catppuccin_mocha",
            IsLight = false,
            BgWindow = 0x181825,
            BgHeader = 0x1B1B29,
            BgCard = 0x1E1E2E,
            BgCardElevated = 0x26263A,
            BgCardHover = 0x2F2F46,
            BgControl = 0x313244,
            BgControlHover = 0x404259,
            BgOsd = 0x1B1B29,
            BorderSubtle = 0x313244,
            BorderStrong = 0x45475A,
            Accent = 0xCBA6F7,
            AccentHover = 0xDBC2FA,
            AccentSoft = 0x342A49,
            OnAccent = 0x181524,
            Green = 0xA6E3A1,
            GreenSoft = 0x25382F,
            Amber = 0xF9E2AF,
            AmberSoft = 0x3F392A,
            Red = 0xF38BA8,
            RedSoft = 0x402634,
            TextPrimary = 0xCDD6F4,
            TextSecondary = 0xA6ADC8,
            TextMuted = 0x7F849C,
            SwitchOffTrack = 0x3E4054,
            SwitchKnob = 0xEFF1F5,
            SwatchBg = 0x1E1E2E,
            SwatchAccent = 0xCBA6F7
        },
        // 8. Tokyo Night
        new ThemePalette
        {
            Id = "tokyo_night",
            IsLight = false,
            BgWindow = 0x16161E,
            BgHeader = 0x191B25,
            BgCard = 0x1A1B26,
            BgCardElevated = 0x222536,
            BgCardHover = 0x292E42,
            BgControl = 0x2D3248,
            BgControlHover = 0x3A415D,
            BgOsd = 0x191B25,
            BorderSubtle = 0x2B3045,
            BorderStrong = 0x3D4463,
            Accent = 0x7AA2F7,
            AccentHover = 0x99B8FA,
            AccentSoft = 0x223052,
            OnAccent = 0x121624,
            Green = 0x9ECE6A,
            GreenSoft = 0x233326,
            Amber = 0xE0AF68,
            AmberSoft = 0x3A3021,
            Red = 0xF7768E,
            RedSoft = 0x3E212E,
            TextPrimary = 0xC0CAF5,
            TextSecondary = 0x9AA5CE,
            TextMuted = 0x6B7599,
            SwitchOffTrack = 0x363C54,
            SwitchKnob = 0xD5DCF8,
            SwatchBg = 0x1A1B26,
            SwatchAccent = 0x7AA2F7
        },
        // 9. Ayu Dark
        new ThemePalette
        {
            Id = "ayu_dark",
            IsLight = false,
            BgWindow = 0x0B0E14,
            BgHeader = 0x0F131A,
            BgCard = 0x11151C,
            BgCardElevated = 0x171D27,
            BgCardHover = 0x1E2532,
            BgControl = 0x222A38,
            BgControlHover = 0x2D384B,
            BgOsd = 0x0F131A,
            BorderSubtle = 0x202734,
            BorderStrong = 0x313B4E,
            Accent = 0xE6B450,
            AccentHover = 0xF0C775,
            AccentSoft = 0x362B16,
            OnAccent = 0x141008,
            Green = 0xAAD94C,
            GreenSoft = 0x223018,
            Amber = 0xFFB454,
            AmberSoft = 0x3A2A15,
            Red = 0xF07178,
            RedSoft = 0x3B1D22,
            TextPrimary = 0xD8D6CF,
            TextSecondary = 0x999B9E,
            TextMuted = 0x626A73,
            SwitchOffTrack = 0x2B3342,
            SwitchKnob = 0xE6E4DD,
            SwatchBg = 0x11151C,
            SwatchAccent = 0xE6B450
        },
        // 10. Ayu Mirage
        new ThemePalette
        {
            Id = "ayu_mirage",
            IsLight = false,
            BgWindow = 0x1A1F29,
            BgHeader = 0x1F2531,
            BgCard = 0x232A37,
            BgCardElevated = 0x2A3242,
            BgCardHover = 0x323C4F,
            BgControl = 0x364156,
            BgControlHover = 0x44516B,
            BgOsd = 0x1F2531,
            BorderSubtle = 0x333D4F,
            BorderStrong = 0x455269,
            Accent = 0xFFCC66,
            AccentHover = 0xFFDA8C,
            AccentSoft = 0x433923,
            OnAccent = 0x1D180D,
            Green = 0x87D96C,
            GreenSoft = 0x243B29,
            Amber = 0xFFD173,
            AmberSoft = 0x443B25,
            Red = 0xF28779,
            RedSoft = 0x43292B,
            TextPrimary = 0xDDDAD2,
            TextSecondary = 0xA6AAB3,
            TextMuted = 0x707A8C,
            SwitchOffTrack = 0x3D485E,
            SwitchKnob = 0xEAE8E1,
            SwatchBg = 0x232A37,
            SwatchAccent = 0xFFCC66
        },
        // 11. One Dark Pro
        new ThemePalette
        {
            Id = "one_dark",
            IsLight = false,
            BgWindow = 0x1E2127,
            BgHeader = 0x23272F,
            BgCard = 0x262B33,
            BgCardElevated = 0x2D333D,
            BgCardHover = 0x353C48,
            BgControl = 0x38404D,
            BgControlHover = 0x465060,
            BgOsd = 0x23272F,
            BorderSubtle = 0x353C48,
            BorderStrong = 0x485161,
            Accent = 0x61AFEF,
            AccentHover = 0x82C1F4,
            AccentSoft = 0x22384D,
            OnAccent = 0x121B24,
            Green = 0x98C379,
            GreenSoft = 0x2A3A2B,
            Amber = 0xE5C07B,
            AmberSoft = 0x423A2A,
            Red = 0xE06C75,
            RedSoft = 0x42262C,
            TextPrimary = 0xD7DAE0,
            TextSecondary = 0x9DA5B4,
            TextMuted = 0x6E7685,
            SwitchOffTrack = 0x414856,
            SwitchKnob = 0xE6E9EF,
            SwatchBg = 0x262B33,
            SwatchAccent = 0x61AFEF
        },
        // 12. Kanagawa Dragon
        new ThemePalette
        {
            Id = "kanagawa",
            IsLight = false,
            BgWindow = 0x181616,
            BgHeader = 0x1D1B1B,
            BgCard = 0x211F1F,
            BgCardElevated = 0x292727,
            BgCardHover = 0x323030,
            BgControl = 0x363434,
            BgControlHover = 0x464343,
            BgOsd = 0x1D1B1B,
            BorderSubtle = 0x333030,
            BorderStrong = 0x484444,
            Accent = 0x8BA4B0,
            AccentHover = 0xA4B9C3,
            AccentSoft = 0x293236,
            OnAccent = 0x141718,
            Green = 0x87A987,
            GreenSoft = 0x273228,
            Amber = 0xC4B28A,
            AmberSoft = 0x393429,
            Red = 0xC4746E,
            RedSoft = 0x3C2625,
            TextPrimary = 0xC5C9C5,
            TextSecondary = 0x9E9B93,
            TextMuted = 0x73706A,
            SwitchOffTrack = 0x3F3C3C,
            SwitchKnob = 0xDCD7BA,
            SwatchBg = 0x211F1F,
            SwatchAccent = 0x8BA4B0
        },
        // 13. Everforest (Dark Medium)
        new ThemePalette
        {
            Id = "everforest",
            IsLight = false,
            BgWindow = 0x232A2E,
            BgHeader = 0x293136,
            BgCard = 0x2D353B,
            BgCardElevated = 0x343F44,
            BgCardHover = 0x3D484D,
            BgControl = 0x414D52,
            BgControlHover = 0x4F5D63,
            BgOsd = 0x293136,
            BorderSubtle = 0x3D484D,
            BorderStrong = 0x515E64,
            Accent = 0xA7C080,
            AccentHover = 0xBBD09B,
            AccentSoft = 0x334031,
            OnAccent = 0x1B211A,
            Green = 0x83C092,
            GreenSoft = 0x2A3F35,
            Amber = 0xDBBC7F,
            AmberSoft = 0x453E2F,
            Red = 0xE67E80,
            RedSoft = 0x472E32,
            TextPrimary = 0xD3C6AA,
            TextSecondary = 0xA7B0A4,
            TextMuted = 0x7A8478,
            SwitchOffTrack = 0x475258,
            SwitchKnob = 0xE6DCC5,
            SwatchBg = 0x2D353B,
            SwatchAccent = 0xA7C080
        },
        // 14. Rosé Pine
        new ThemePalette
        {
            Id = "rose_pine",
            IsLight = false,
            BgWindow = 0x191724,
            BgHeader = 0x1D1B2A,
            BgCard = 0x1F1D2E,
            BgCardElevated = 0x26233A,
            BgCardHover = 0x2F2B46,
            BgControl = 0x332F4B,
            BgControlHover = 0x423D5E,
            BgOsd = 0x1D1B2A,
            BorderSubtle = 0x312D48,
            BorderStrong = 0x464163,
            Accent = 0xEBBCBA,
            AccentHover = 0xF3CFCB,
            AccentSoft = 0x3E2E3A,
            OnAccent = 0x1B1622,
            Green = 0x9CCFD8,
            GreenSoft = 0x243640,
            Amber = 0xF6C177,
            AmberSoft = 0x41352A,
            Red = 0xEB6F92,
            RedSoft = 0x422434,
            TextPrimary = 0xE0DEF4,
            TextSecondary = 0xA5A0BD,
            TextMuted = 0x6E6A86,
            SwitchOffTrack = 0x3B3754,
            SwitchKnob = 0xEFEDFB,
            SwatchBg = 0x1F1D2E,
            SwatchAccent = 0xEBBCBA
        },
        // 15. Light (Default — Soft Fluent Alabaster)
        new ThemePalette
        {
            Id = "light",
            IsLight = true,
            BgWindow = 0xF1F3F7,
            BgHeader = 0xFAFBFC,
            BgCard = 0xFFFFFF,
            BgCardElevated = 0xF6F8FB,
            BgCardHover = 0xECEFF5,
            BgControl = 0xE4E8F0,
            BgControlHover = 0xD5DCE8,
            BgOsd = 0xFFFFFF,
            BorderSubtle = 0xDCE2EC,
            BorderStrong = 0xC2CBDB,
            Accent = 0x3B6FE8,
            AccentHover = 0x2C5CD4,
            AccentSoft = 0xE2EBFE,
            OnAccent = 0xFFFFFF,
            Green = 0x168A58,
            GreenSoft = 0xDCF5E8,
            Amber = 0xC2780A,
            AmberSoft = 0xFCEFD3,
            Red = 0xCC3B48,
            RedSoft = 0xFCE2E5,
            TextPrimary = 0x161B26,
            TextSecondary = 0x525C6E,
            TextMuted = 0x7A8499,
            SwitchOffTrack = 0xC9D1DE,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xF1F3F7,
            SwatchAccent = 0x3B6FE8
        },
        // 16. Catppuccin Latte
        new ThemePalette
        {
            Id = "catppuccin_latte",
            IsLight = true,
            BgWindow = 0xE6E9EF,
            BgHeader = 0xEFF1F5,
            BgCard = 0xF6F7FA,
            BgCardElevated = 0xEFF1F5,
            BgCardHover = 0xE2E5ED,
            BgControl = 0xDCE0E8,
            BgControlHover = 0xCCD0DA,
            BgOsd = 0xF6F7FA,
            BorderSubtle = 0xD2D6E0,
            BorderStrong = 0xB8BECC,
            Accent = 0x8839EF,
            AccentHover = 0x7427DC,
            AccentSoft = 0xEADDFC,
            OnAccent = 0xFFFFFF,
            Green = 0x40A02B,
            GreenSoft = 0xDDF2D8,
            Amber = 0xDF8E1D,
            AmberSoft = 0xFAECD5,
            Red = 0xD20F39,
            RedSoft = 0xFADCE2,
            TextPrimary = 0x4C4F69,
            TextSecondary = 0x6C6F85,
            TextMuted = 0x8C8FA1,
            SwitchOffTrack = 0xBCC0CC,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xEFF1F5,
            SwatchAccent = 0x8839EF
        },
        // 17. Rosé Pine Dawn
        new ThemePalette
        {
            Id = "rose_pine_dawn",
            IsLight = true,
            BgWindow = 0xF2E9E1,
            BgHeader = 0xFAF4ED,
            BgCard = 0xFFFBF6,
            BgCardElevated = 0xF6EFE7,
            BgCardHover = 0xEDE3D9,
            BgControl = 0xE8DDD2,
            BgControlHover = 0xDDD0C2,
            BgOsd = 0xFAF4ED,
            BorderSubtle = 0xE2D6CA,
            BorderStrong = 0xCBBBAF,
            Accent = 0xD7827E,
            AccentHover = 0xC46C68,
            AccentSoft = 0xF6E2DF,
            OnAccent = 0xFFFFFF,
            Green = 0x286983,
            GreenSoft = 0xDCE9EE,
            Amber = 0xEA9D34,
            AmberSoft = 0xFBECD7,
            Red = 0xB4637A,
            RedSoft = 0xF4E0E6,
            TextPrimary = 0x575279,
            TextSecondary = 0x797593,
            TextMuted = 0x9893A5,
            SwitchOffTrack = 0xCECACD,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xFAF4ED,
            SwatchAccent = 0xD7827E
        },
        // 18. Everforest Light Soft
        new ThemePalette
        {
            Id = "everforest_light",
            IsLight = true,
            BgWindow = 0xEFEBD4,
            BgHeader = 0xF6F2DF,
            BgCard = 0xFAF6E8,
            BgCardElevated = 0xF3EEDB,
            BgCardHover = 0xEAE4CC,
            BgControl = 0xE4DEC5,
            BgControlHover = 0xD8D1B5,
            BgOsd = 0xF6F2DF,
            BorderSubtle = 0xDDD7BD,
            BorderStrong = 0xC4BDA2,
            Accent = 0x8DA101,
            AccentHover = 0x768700,
            AccentSoft = 0xE9EBC8,
            OnAccent = 0xFFFFFF,
            Green = 0x35A77C,
            GreenSoft = 0xD9EFE5,
            Amber = 0xDFA000,
            AmberSoft = 0xF8ECCA,
            Red = 0xF85552,
            RedSoft = 0xFCE0DD,
            TextPrimary = 0x4E5B62,
            TextSecondary = 0x6F7D80,
            TextMuted = 0x8F9A96,
            SwitchOffTrack = 0xC5C1A8,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xF6F2DF,
            SwatchAccent = 0x8DA101
        },
        // 19. Ayu Light
        new ThemePalette
        {
            Id = "ayu_light",
            IsLight = true,
            BgWindow = 0xF3F4F5,
            BgHeader = 0xFAFAFA,
            BgCard = 0xFFFFFF,
            BgCardElevated = 0xF7F8F9,
            BgCardHover = 0xECEEF0,
            BgControl = 0xE6E9EC,
            BgControlHover = 0xD8DDE3,
            BgOsd = 0xFFFFFF,
            BorderSubtle = 0xDFE3E8,
            BorderStrong = 0xC5CCD4,
            Accent = 0xE68A00,
            AccentHover = 0xCC7A00,
            AccentSoft = 0xFDEED6,
            OnAccent = 0xFFFFFF,
            Green = 0x6CBF43,
            GreenSoft = 0xE3F4DB,
            Amber = 0xE6A100,
            AmberSoft = 0xFCF1D2,
            Red = 0xE65050,
            RedSoft = 0xFCDDDD,
            TextPrimary = 0x4A525A,
            TextSecondary = 0x707A85,
            TextMuted = 0x959FA9,
            SwitchOffTrack = 0xCBD1D8,
            SwitchKnob = 0xFFFFFF,
            SwatchBg = 0xFAFAFA,
            SwatchAccent = 0xE68A00
        },
        // 20. Gruvbox Light Soft
        new ThemePalette
        {
            Id = "gruvbox_light",
            IsLight = true,
            BgWindow = 0xEBDBB2,
            BgHeader = 0xF2E5BC,
            BgCard = 0xF9F0D4,
            BgCardElevated = 0xF2E5BC,
            BgCardHover = 0xE5D4A9,
            BgControl = 0xDFCFA2,
            BgControlHover = 0xD2C090,
            BgOsd = 0xF2E5BC,
            BorderSubtle = 0xD5C4A1,
            BorderStrong = 0xBDAE93,
            Accent = 0xAF3A03,
            AccentHover = 0x943103,
            AccentSoft = 0xF2D4BF,
            OnAccent = 0xFFFFFF,
            Green = 0x79740E,
            GreenSoft = 0xE6E3BF,
            Amber = 0xB57614,
            AmberSoft = 0xF3E2BE,
            Red = 0x9D0006,
            RedSoft = 0xF0CECA,
            TextPrimary = 0x3C3836,
            TextSecondary = 0x665C54,
            TextMuted = 0x7C6F64,
            SwitchOffTrack = 0xC5B596,
            SwitchKnob = 0xFBF1C7,
            SwatchBg = 0xF2E5BC,
            SwatchAccent = 0xAF3A03
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
        string key = id.Trim().ToLowerInvariant() switch
        {
            "emerald" => "everforest",
            "amethyst" => "dracula",
            "amber" => "gruvbox_material",
            "crimson" => "rose_pine",
            "ocean" => "tokyo_night",
            "sakura" => "catppuccin_mocha",
            "sand" => "gruvbox_light",
            "slate" => "catppuccin_latte",
            var other => other
        };
        var found = FindById(key);
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
