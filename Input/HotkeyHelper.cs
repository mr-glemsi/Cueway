using System.Text;
using Cueway.Interop;

namespace Cueway.Input;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

internal readonly record struct HotkeyBinding(HotkeyModifiers Modifiers, uint VkCode, string NormalizedText)
{
    public bool IsEmpty => VkCode == 0 || string.IsNullOrEmpty(NormalizedText);
}

internal static class HotkeyHelper
{
    public static bool IsModifierVk(uint vk) => vk is
        Win32.VK_SHIFT or Win32.VK_LSHIFT or Win32.VK_RSHIFT or
        Win32.VK_CONTROL or Win32.VK_LCONTROL or Win32.VK_RCONTROL or
        Win32.VK_MENU or Win32.VK_LMENU or Win32.VK_RMENU or
        Win32.VK_LWIN or Win32.VK_RWIN;

    public static bool IsMediaVk(uint vk) => vk is
        Win32.VK_MEDIA_NEXT_TRACK or Win32.VK_MEDIA_PREV_TRACK or
        Win32.VK_MEDIA_STOP or Win32.VK_MEDIA_PLAY_PAUSE;

    public static bool IsAllowedWithoutModifier(uint vk)
    {
        if (vk >= Win32.VK_F1 && vk <= Win32.VK_F24)
            return true;
        if (vk >= Win32.VK_NUMPAD0 && vk <= Win32.VK_DIVIDE)
            return true;

        return vk is Win32.VK_INSERT or Win32.VK_HOME or Win32.VK_END or
            Win32.VK_PRIOR or Win32.VK_NEXT or Win32.VK_PAUSE or
            Win32.VK_SCROLL or Win32.VK_SNAPSHOT or Win32.VK_APPS;
    }

    public static bool TryCreate(
        HotkeyModifiers modifiers,
        uint vkCode,
        out HotkeyBinding binding,
        out bool requiresModifier,
        bool allowAnySingleKey = false)
    {
        binding = default;
        requiresModifier = false;

        if (vkCode == 0 || vkCode == Win32.VK_ESCAPE || IsModifierVk(vkCode) || IsMediaVk(vkCode))
            return false;

        if (!TryFormatVk(vkCode, out string keyToken))
            return false;

        if (!allowAnySingleKey && modifiers == HotkeyModifiers.None && !IsAllowedWithoutModifier(vkCode))
        {
            requiresModifier = true;
            return false;
        }

        string formatted = FormatTokens(modifiers, keyToken, spaced: false);
        binding = new HotkeyBinding(modifiers, vkCode, formatted);
        return true;
    }

    public static bool TryParse(string? text, out HotkeyBinding binding, bool allowAnySingleKey = false)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string trimmed = text.Trim();
        HotkeyModifiers mods = HotkeyModifiers.None;
        uint vk = 0;

        int i = 0;
        while (i < trimmed.Length)
        {
            while (i < trimmed.Length && (trimmed[i] == '+' || char.IsWhiteSpace(trimmed[i])))
                i++;
            if (i >= trimmed.Length)
                break;

            string token;
            if (trimmed.AsSpan(i).StartsWith("Num+", StringComparison.OrdinalIgnoreCase) &&
                (i + 4 == trimmed.Length || char.IsWhiteSpace(trimmed[i + 4]) || trimmed[i + 4] == '+'))
            {
                token = "Num+";
                i += 4;
            }
            else
            {
                int nextPlus = trimmed.IndexOf('+', i);
                token = (nextPlus >= 0 ? trimmed[i..nextPlus] : trimmed[i..]).Trim();
                i = nextPlus >= 0 ? nextPlus + 1 : trimmed.Length;
            }

            if (token.Length == 0)
                continue;

            if (TryParseModifier(token, out var mod))
            {
                mods |= mod;
                continue;
            }

            if (vk != 0 || !TryParseVk(token, out vk))
                return false;
        }

        return TryCreate(mods, vk, out binding, out _, allowAnySingleKey);
    }

    public static string Normalize(string? text, bool allowAnySingleKey = false)
        => TryParse(text, out var binding, allowAnySingleKey) ? binding.NormalizedText : string.Empty;

    public static string FormatModifierPreview(HotkeyModifiers modifiers)
    {
        if (modifiers == HotkeyModifiers.None)
            return string.Empty;

        var sb = new StringBuilder();
        AppendModifiers(sb, modifiers, " + ");
        sb.Append(" + ...");
        return sb.ToString();
    }

    public static string FormatDisplay(string? normalized, bool spaced = false, bool allowAnySingleKey = false)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (!TryParse(normalized, out var binding, allowAnySingleKey))
            return normalized.Trim();

        if (!spaced || !TryFormatVk(binding.VkCode, out string keyToken))
            return binding.NormalizedText;

        return FormatTokens(binding.Modifiers, keyToken, spaced: true);
    }

    private static string FormatTokens(HotkeyModifiers modifiers, string keyToken, bool spaced)
    {
        string sep = spaced ? " + " : "+";
        var sb = new StringBuilder();
        AppendModifiers(sb, modifiers, sep);
        if (sb.Length > 0)
            sb.Append(sep);
        sb.Append(keyToken);
        return sb.ToString();
    }

    private static void AppendModifiers(StringBuilder sb, HotkeyModifiers modifiers, string sep)
    {
        if ((modifiers & HotkeyModifiers.Ctrl) != 0)
        {
            sb.Append("Ctrl");
        }
        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            if (sb.Length > 0) sb.Append(sep);
            sb.Append("Alt");
        }
        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            if (sb.Length > 0) sb.Append(sep);
            sb.Append("Shift");
        }
        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            if (sb.Length > 0) sb.Append(sep);
            sb.Append("Win");
        }
    }

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
            "ALT" or "MENU" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" or "SUPER" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None
        };
        return modifier != HotkeyModifiers.None;
    }

    public static bool TryFormatVk(uint vk, out string token)
    {
        if (vk >= 'A' && vk <= 'Z')
        {
            token = ((char)vk).ToString();
            return true;
        }
        if (vk >= '0' && vk <= '9')
        {
            token = ((char)vk).ToString();
            return true;
        }
        if (vk >= Win32.VK_F1 && vk <= Win32.VK_F24)
        {
            token = $"F{vk - Win32.VK_F1 + 1}";
            return true;
        }
        if (vk >= Win32.VK_NUMPAD0 && vk <= Win32.VK_NUMPAD9)
        {
            token = $"Num{vk - Win32.VK_NUMPAD0}";
            return true;
        }

        token = (int)vk switch
        {
            Win32.VK_SPACE => "Space",
            Win32.VK_TAB => "Tab",
            Win32.VK_RETURN => "Enter",
            Win32.VK_BACK => "Backspace",
            Win32.VK_DELETE => "Delete",
            Win32.VK_INSERT => "Insert",
            Win32.VK_HOME => "Home",
            Win32.VK_END => "End",
            Win32.VK_PRIOR => "PageUp",
            Win32.VK_NEXT => "PageDown",
            Win32.VK_UP => "Up",
            Win32.VK_DOWN => "Down",
            Win32.VK_LEFT => "Left",
            Win32.VK_RIGHT => "Right",
            Win32.VK_PAUSE => "Pause",
            Win32.VK_SCROLL => "ScrollLock",
            Win32.VK_SNAPSHOT => "PrintScreen",
            Win32.VK_APPS => "Menu",
            Win32.VK_MULTIPLY => "Num*",
            Win32.VK_ADD => "Num+",
            Win32.VK_SUBTRACT => "Num-",
            Win32.VK_DECIMAL => "Num.",
            Win32.VK_DIVIDE => "Num/",
            Win32.VK_OEM_MINUS => "-",
            Win32.VK_OEM_PLUS => "=",
            Win32.VK_OEM_4 => "[",
            Win32.VK_OEM_6 => "]",
            Win32.VK_OEM_5 => "\\",
            Win32.VK_OEM_1 => ";",
            Win32.VK_OEM_7 => "'",
            Win32.VK_OEM_COMMA => ",",
            Win32.VK_OEM_PERIOD => ".",
            Win32.VK_OEM_2 => "/",
            Win32.VK_OEM_3 => "`",
            Win32.VK_OEM_8 => "OEM8",
            Win32.VK_OEM_102 => "<>",
            _ => string.Empty
        };
        return token.Length > 0;
    }

    private static bool TryParseVk(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        string upper = token.ToUpperInvariant();
        if (upper.StartsWith('F') && upper.Length is >= 2 and <= 3 &&
            int.TryParse(upper.AsSpan(1), out int fNum) && fNum is >= 1 and <= 24)
        {
            vk = (uint)(Win32.VK_F1 + fNum - 1);
            return true;
        }

        if (upper.StartsWith("NUM", StringComparison.Ordinal) && upper.Length == 4 &&
            upper[3] is >= '0' and <= '9')
        {
            vk = (uint)(Win32.VK_NUMPAD0 + (upper[3] - '0'));
            return true;
        }

        vk = upper switch
        {
            "SPACE" or "SPACEBAR" => Win32.VK_SPACE,
            "TAB" => Win32.VK_TAB,
            "ENTER" or "RETURN" => Win32.VK_RETURN,
            "BACKSPACE" or "BACK" => Win32.VK_BACK,
            "DELETE" or "DEL" => Win32.VK_DELETE,
            "INSERT" or "INS" => Win32.VK_INSERT,
            "HOME" => Win32.VK_HOME,
            "END" => Win32.VK_END,
            "PAGEUP" or "PGUP" => Win32.VK_PRIOR,
            "PAGEDOWN" or "PGDN" => Win32.VK_NEXT,
            "UP" => Win32.VK_UP,
            "DOWN" => Win32.VK_DOWN,
            "LEFT" => Win32.VK_LEFT,
            "RIGHT" => Win32.VK_RIGHT,
            "PAUSE" or "BREAK" => Win32.VK_PAUSE,
            "SCROLLLOCK" or "SCROLL" => Win32.VK_SCROLL,
            "PRINTSCREEN" or "PRTSC" or "SNAPSHOT" => Win32.VK_SNAPSHOT,
            "MENU" or "APPS" => Win32.VK_APPS,
            "NUM*" or "MULTIPLY" => Win32.VK_MULTIPLY,
            "NUM+" or "ADD" => Win32.VK_ADD,
            "NUM-" or "SUBTRACT" => Win32.VK_SUBTRACT,
            "NUM." or "DECIMAL" => Win32.VK_DECIMAL,
            "NUM/" or "DIVIDE" => Win32.VK_DIVIDE,
            "-" or "MINUS" => Win32.VK_OEM_MINUS,
            "=" or "PLUS" or "EQUALS" => Win32.VK_OEM_PLUS,
            "[" => Win32.VK_OEM_4,
            "]" => Win32.VK_OEM_6,
            "\\" => Win32.VK_OEM_5,
            ";" => Win32.VK_OEM_1,
            "'" => Win32.VK_OEM_7,
            "," or "COMMA" => Win32.VK_OEM_COMMA,
            "." or "PERIOD" => Win32.VK_OEM_PERIOD,
            "/" => Win32.VK_OEM_2,
            "`" => Win32.VK_OEM_3,
            "OEM8" => Win32.VK_OEM_8,
            "<>" => Win32.VK_OEM_102,
            _ => 0
        };
        return vk != 0;
    }
}
