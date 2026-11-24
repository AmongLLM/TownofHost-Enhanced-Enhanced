using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TOHE;

internal static class ChatRichText
{
    private const RegexOptions TagOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Tags = new(@"<noparse>[\s\S]*?(?:</noparse>|\z)|<[^<>]+>", TagOptions);
    private static readonly Regex HexColor = new(@"^<(?:color=)?#([0-9a-f]{3,4}|[0-9a-f]{6}(?:[0-9a-f]{2})?)>$", TagOptions);
    private static readonly Regex Number = new(
        @"^<(size|voffset|cspace|mspace|line-height|indent|line-indent|pos|width|margin(?:-left|-right)?|space|sprite index|sprite)=([+-]?)([0-9]+)(?:\.([0-9]+))?(px|em|%)?>$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Compact(string text, string field)
    {
        text ??= string.Empty;
        int shortenedTags = 0;
        string compact = Tags.Replace(text, match =>
        {
            string replacement = CompactTag(match.Value);
            if (replacement.Length < match.Length) shortenedTags++;
            return replacement;
        });

        int inputBytes = Encoding.UTF8.GetByteCount(text);
        int outputBytes = Encoding.UTF8.GetByteCount(compact);
        Logger.Info($"field={field}, inputUtf8Bytes={inputBytes}, outputUtf8Bytes={outputBytes}, " +
            $"savedUtf8Bytes={inputBytes - outputBytes}, shortenedTags={shortenedTags}", nameof(ChatRichText));
        return compact;
    }

    internal static bool IsShorthandColor(string tag)
        => tag.StartsWith("<#", StringComparison.Ordinal) && HexColor.IsMatch(tag);

    private static string CompactTag(string tag)
    {
        switch (tag.ToLowerInvariant())
        {
            case "<br>": return "\n";
            case "<uppercase>": return "<allcaps>";
            case "</uppercase>": return "</allcaps>";
        }

        var color = HexColor.Match(tag);
        if (color.Success) return CompactColor(color.Groups[1].Value);

        var number = Number.Match(tag);
        if (!number.Success) return tag;

        string name = number.Groups[1].Value;
        string sign = number.Groups[2].Value;
        string whole = number.Groups[3].Value.TrimStart('0');
        string fraction = number.Groups[4].Value.TrimEnd('0');
        string unit = number.Groups[5].Value;

        if (name is "sprite" or "sprite index")
        {
            if (unit.Length != 0 || number.Groups[4].Success) return tag;
            name = "sprite";
        }

        if (unit == "px") unit = string.Empty;
        string value = fraction.Length > 0 ? $"{whole}.{fraction}" : whole.Length > 0 ? whole : "0";
        return $"<{name}={sign}{value}{unit}>";
    }

    private static string CompactColor(string hex)
    {
        if (hex.Length == 8 && hex.EndsWith("ff", StringComparison.OrdinalIgnoreCase)) hex = hex[..6];
        if (hex.Length == 4 && hex.EndsWith("f", StringComparison.OrdinalIgnoreCase)) hex = hex[..3];
        if (hex.Length < 6) return $"<#{hex}>";

        // Each short digit expands to a repeated pair, including the optional alpha channel.
        Span<char> shortHex = stackalloc char[4];
        int pairs = hex.Length / 2;
        for (int i = 0; i < pairs; i++)
        {
            char first = hex[i * 2], second = hex[i * 2 + 1];
            if (char.ToUpperInvariant(first) != char.ToUpperInvariant(second)) return $"<#{hex}>";
            shortHex[i] = first;
        }
        return $"<#{new string(shortHex[..pairs])}>";
    }
}
