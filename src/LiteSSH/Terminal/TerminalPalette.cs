namespace LiteSSH.Terminal;

public sealed class TerminalPalette
{
    private static readonly uint[] CubeLevels = { 0, 95, 135, 175, 215, 255 };
    private static readonly uint[] Cube = BuildCube();

    public string Id { get; init; } = "miuix-dark";
    public string DisplayZh { get; init; } = "";
    public string DisplayEn { get; init; } = "";
    public bool Dark { get; init; }
    public uint Background { get; init; }
    public uint Foreground { get; init; }
    public uint Cursor { get; init; }
    public uint Selection { get; init; }
    public uint[] Ansi { get; init; } = Array.Empty<uint>();

    public string Display => Core.L10n.IsEnglish ? DisplayEn : DisplayZh;

    private static uint[] BuildCube()
    {
        var list = new uint[256];
        for (var i = 0; i < 16; i++)
        {
            list[i] = 0;
        }
        for (var i = 16; i < 232; i++)
        {
            var idx = i - 16;
            var r = CubeLevels[idx / 36];
            var g = CubeLevels[idx % 36 / 6];
            var b = CubeLevels[idx % 6];
            list[i] = (r << 16) | (g << 8) | b;
        }
        for (var i = 232; i < 256; i++)
        {
            var v = (uint)(8 + (i - 232) * 10);
            list[i] = (v << 16) | (v << 8) | v;
        }
        return list;
    }

    public static readonly TerminalPalette MiuixDark = new()
    {
        Id = "miuix-dark",
        DisplayZh = "Miuix 深色",
        DisplayEn = "Miuix Dark",
        Dark = true,
        Background = 0x101014,
        Foreground = 0xE8E8EF,
        Cursor = 0x6AA1FF,
        Selection = 0x2B3A57,
        Ansi = new uint[]
        {
            0x26262E, 0xF16C6C, 0x5CD68A, 0xE8C468, 0x6AA1FF, 0xC58CFF, 0x53D2E0, 0xC8C8D2,
            0x55555F, 0xFF8A8A, 0x7BE8A6, 0xFFDA8A, 0x95BEFF, 0xDDB4FF, 0x8AE8F2, 0xFFFFFF,
        },
    };

    public static readonly TerminalPalette MiuixLight = new()
    {
        Id = "miuix-light",
        DisplayZh = "Miuix 浅色",
        DisplayEn = "Miuix Light",
        Dark = false,
        Background = 0xFDFDFE,
        Foreground = 0x1B1B20,
        Cursor = 0x3482FF,
        Selection = 0xD5E3FF,
        Ansi = new uint[]
        {
            0x2E2E36, 0xE05252, 0x2E9E5B, 0xB07800, 0x2A6FD6, 0x8A4FD3, 0x0E8F9E, 0x8A8A94,
            0x666670, 0xD94141, 0x1F8A4C, 0x9C6A00, 0x1F5FC0, 0x7B3FBF, 0x0B7A88, 0x000000,
        },
    };

    public static readonly TerminalPalette SolarizedDark = new()
    {
        Id = "solarized-dark",
        DisplayZh = "Solarized 深色",
        DisplayEn = "Solarized Dark",
        Dark = true,
        Background = 0x002B36,
        Foreground = 0x93A1A1,
        Cursor = 0x93A1A1,
        Selection = 0x0B4A5C,
        Ansi = new uint[]
        {
            0x073642, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5,
            0x002B36, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3,
        },
    };

    public static readonly TerminalPalette OneDark = new()
    {
        Id = "one-dark",
        DisplayZh = "One Dark",
        DisplayEn = "One Dark",
        Dark = true,
        Background = 0x1E2127,
        Foreground = 0xABB2BF,
        Cursor = 0x528BFF,
        Selection = 0x3E4451,
        Ansi = new uint[]
        {
            0x282C34, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xABB2BF,
            0x5C6370, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xFFFFFF,
        },
    };

    public static readonly TerminalPalette Nord = new()
    {
        Id = "nord",
        DisplayZh = "Nord",
        DisplayEn = "Nord",
        Dark = true,
        Background = 0x2E3440,
        Foreground = 0xD8DEE9,
        Cursor = 0x88C0D0,
        Selection = 0x434C5E,
        Ansi = new uint[]
        {
            0x3B4252, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x88C0D0, 0xE5E9F0,
            0x4C566A, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x8FBCBB, 0xECEFF4,
        },
    };

    public static readonly TerminalPalette[] All = { MiuixDark, MiuixLight, SolarizedDark, OneDark, Nord };

    public static TerminalPalette Resolve(string id, bool darkUi)
    {
        if (string.IsNullOrEmpty(id) || id == "miuix-auto")
        {
            return darkUi ? MiuixDark : MiuixLight;
        }
        return All.FirstOrDefault(p => p.Id == id) ?? (darkUi ? MiuixDark : MiuixLight);
    }

    /// <summary>Encodes: -1 = default, 0..255 palette, >= 0x1000000 packed RGB.</summary>
    public uint Color(int value)
    {
        if (value < 0)
        {
            return Foreground;
        }
        if (value >= 0x1000000)
        {
            return (uint)(value & 0xFFFFFF);
        }
        if (value < 16)
        {
            return Ansi[value];
        }
        return Cube[value & 0xFF];
    }

    public static uint Mix(uint background, uint foreground, double alpha)
    {
        var br = (background >> 16) & 0xFF;
        var bg = (background >> 8) & 0xFF;
        var bb = background & 0xFF;
        var fr = (foreground >> 16) & 0xFF;
        var fg = (foreground >> 8) & 0xFF;
        var fb = foreground & 0xFF;
        var r = (uint)(br + (fr - br) * alpha);
        var g = (uint)(bg + (fg - bg) * alpha);
        var b = (uint)(bb + (fb - bb) * alpha);
        return (r << 16) | (g << 8) | b;
    }
}
