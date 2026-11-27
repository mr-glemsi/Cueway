using Cueway.Audio;

namespace Cueway.UI;

internal static class AudioDeviceColors
{
    private static readonly uint[] DarkPalette =
    [
        0x38BDF8u, // Blue (Headset/Headphone preferred)
        0xF43F5Eu, // Red (Speaker/Monitor preferred)
        0x34D399u, // Emerald (Earbuds/In-Ear preferred)
        0xF59E0Bu, // Amber
        0xA855F7u, // Purple
        0xEC4899u, // Pink
        0x2DD4BFu, // Teal
        0xA3E635u, // Lime
        0x818CF8u, // Indigo
        0xFB923Cu  // Orange
    ];

    private static readonly uint[] LightPalette =
    [
        0x0284C7u, // Blue
        0xDC2626u, // Red
        0x059669u, // Emerald
        0xD97706u, // Amber
        0x9333EAu, // Purple
        0xDB2777u, // Pink
        0x0D9488u, // Teal
        0x65A30Du, // Lime
        0x4F46E5u, // Indigo
        0xEA580Cu  // Orange
    ];

    public static string? GetDefaultDeviceId(IReadOnlyList<AudioOutputDevice> devices)
    {
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i].IsDefault)
                return devices[i].Id;
        }
        return devices.Count > 0 ? devices[0].Id : null;
    }

    public static Dictionary<string, uint> BuildDeviceColorMap(IReadOnlyList<AudioOutputDevice> devices, bool isLightTheme)
    {
        var map = new Dictionary<string, uint>(devices.Count, StringComparer.OrdinalIgnoreCase);
        if (devices.Count == 0)
            return map;

        uint[] palette = isLightTheme ? LightPalette : DarkPalette;

        // Sort canonically by device ID so colors remain stable even when Windows default changes
        var sorted = new List<AudioOutputDevice>(devices.Count);
        for (int i = 0; i < devices.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(devices[i].Id))
                sorted.Add(devices[i]);
        }
        sorted.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));

        var usedIndices = new HashSet<int>();
        var unassigned = new List<AudioOutputDevice>();

        // Pass 1: Semantic preference (Headset -> Blue, Speaker -> Red, Earbuds -> Emerald)
        foreach (var dev in sorted)
        {
            int preferred = GetPreferredPaletteIndex(dev.Name);
            if (preferred >= 0 && usedIndices.Add(preferred))
            {
                map[dev.Id] = palette[preferred];
            }
            else
            {
                unassigned.Add(dev);
            }
        }

        // Pass 2: Assign remaining devices to lowest unused slot (curated palette, then unlimited golden-angle HSL)
        int nextIdx = 0;
        foreach (var dev in unassigned)
        {
            while (usedIndices.Contains(nextIdx))
                nextIdx++;

            usedIndices.Add(nextIdx);
            map[dev.Id] = nextIdx < palette.Length
                ? palette[nextIdx]
                : HslToRgb((195f + nextIdx * 137.508f) % 360f, isLightTheme ? 0.72f : 0.80f, isLightTheme ? 0.44f : 0.62f);
            nextIdx++;
        }

        return map;
    }

    private static int GetPreferredPaletteIndex(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return -1;

        string lower = name.ToLowerInvariant();

        if (ContainsAny(lower, "earbud", "airpod", "buds", "in-ear", "tws", "kulak içi", "kulak ici", "freebuds", "wf-"))
            return 2; // Emerald

        if (ContainsAny(lower, "headset", "headphone", "earphone", "kulaklık", "kulaklik", "hyperx", "steelseries", "razer", "corsair", "arctis", "cloud"))
            return 0; // Blue

        if (ContainsAny(lower, "speaker", "hoparlör", "hoparlor", "realtek", "line out", "hdmi", "displayport", "soundbar", "monitor", "tv", "high definition"))
            return 1; // Red

        return -1;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (string term in terms)
        {
            if (text.Contains(term, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static uint HslToRgb(float hueDeg, float sat, float light)
    {
        float h = ((hueDeg % 360f) + 360f) % 360f / 360f;
        float q = light < 0.5f ? light * (1f + sat) : light + sat - light * sat;
        float p = 2f * light - q;

        float r = HueToRgb(p, q, h + 1f / 3f);
        float g = HueToRgb(p, q, h);
        float b = HueToRgb(p, q, h - 1f / 3f);

        uint ri = (uint)Math.Clamp((int)MathF.Round(r * 255f), 0, 255);
        uint gi = (uint)Math.Clamp((int)MathF.Round(g * 255f), 0, 255);
        uint bi = (uint)Math.Clamp((int)MathF.Round(b * 255f), 0, 255);
        return (ri << 16) | (gi << 8) | bi;
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
    }
}
