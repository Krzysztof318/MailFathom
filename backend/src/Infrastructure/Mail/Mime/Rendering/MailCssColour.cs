// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Resolves the colour notations mail actually writes into the three channels the contract carries.</summary>
/// <remarks>
/// <para>
/// The reduction resolves a colour rather than forwarding whatever the message wrote, so nothing downstream parses CSS
/// and no notation nobody implemented can arrive at a renderer as text. What is not resolved here is simply absent from
/// the document, which is the safe direction: a message whose colour this does not understand is drawn in the pane's
/// own text colour rather than in something guessed.
/// </para>
/// <para>
/// The keyword table is the sixteen colours HTML 4 named plus the handful mail templates reach for by name. It is
/// deliberately not the full CSS list: every keyword is a name a reviewer has to be able to check, and a message using
/// <c>rebeccapurple</c> loses a colour rather than anything else.
/// </para>
/// <para>
/// Alpha is read and discarded rather than refused, because <c>rgba(0,0,0,0.87)</c> is how mail writes ordinary body
/// text. What it must not do is decide legibility, so the colour is taken as opaque and the pane composes it against
/// whichever theme the reader is in.
/// </para>
/// </remarks>
internal static class MailCssColour
{
    /// <summary>The longest notation this reads, past which the value is a body trying to cost a parse rather than name a colour.</summary>
    private const int MaximumNotationLength = 64;

    private static readonly Dictionary<string, MailDocumentColour> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0x00, 0x00, 0x00),
        ["silver"] = new(0xC0, 0xC0, 0xC0),
        ["gray"] = new(0x80, 0x80, 0x80),
        ["grey"] = new(0x80, 0x80, 0x80),
        ["white"] = new(0xFF, 0xFF, 0xFF),
        ["maroon"] = new(0x80, 0x00, 0x00),
        ["red"] = new(0xFF, 0x00, 0x00),
        ["purple"] = new(0x80, 0x00, 0x80),
        ["fuchsia"] = new(0xFF, 0x00, 0xFF),
        ["green"] = new(0x00, 0x80, 0x00),
        ["lime"] = new(0x00, 0xFF, 0x00),
        ["olive"] = new(0x80, 0x80, 0x00),
        ["yellow"] = new(0xFF, 0xFF, 0x00),
        ["navy"] = new(0x00, 0x00, 0x80),
        ["blue"] = new(0x00, 0x00, 0xFF),
        ["teal"] = new(0x00, 0x80, 0x80),
        ["aqua"] = new(0x00, 0xFF, 0xFF),
        ["cyan"] = new(0x00, 0xFF, 0xFF),
        ["magenta"] = new(0xFF, 0x00, 0xFF),
        ["orange"] = new(0xFF, 0xA5, 0x00),
        ["darkgray"] = new(0xA9, 0xA9, 0xA9),
        ["darkgrey"] = new(0xA9, 0xA9, 0xA9),
        ["lightgray"] = new(0xD3, 0xD3, 0xD3),
        ["lightgrey"] = new(0xD3, 0xD3, 0xD3),
        ["whitesmoke"] = new(0xF5, 0xF5, 0xF5),
        ["transparent"] = new(0xFF, 0xFF, 0xFF),
    };

    /// <summary>Resolves one colour notation.</summary>
    /// <param name="notation">The notation as the message wrote it.</param>
    /// <returns>The colour, or <see langword="null" /> where the notation is not one this resolves.</returns>
    internal static MailDocumentColour? Resolve(string? notation)
    {
        if (notation is null)
        {
            return null;
        }

        var value = notation.Trim();
        if (value.Length is 0 or > MaximumNotationLength)
        {
            return null;
        }

        if (value[0] == '#')
        {
            return ResolveHexadecimal(value.AsSpan(1));
        }

        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveFunction(value);
        }

        return Keywords.TryGetValue(value, out var keyword) ? keyword : null;
    }

    /// <summary>Reads the three- and six-digit hexadecimal notations, and the alpha-carrying forms beside them.</summary>
    private static MailDocumentColour? ResolveHexadecimal(ReadOnlySpan<char> digits)
    {
        return digits.Length switch
        {
            3 or 4 => Channels(
                Doubled(digits[0]),
                Doubled(digits[1]),
                Doubled(digits[2])),
            6 or 8 => Channels(
                Pair(digits[..2]),
                Pair(digits[2..4]),
                Pair(digits[4..6])),
            _ => null,
        };

        static int? Doubled(char digit) => Pair([digit, digit]);

        static int? Pair(ReadOnlySpan<char> pair) =>
            byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var channel)
                ? channel
                : null;
    }

    /// <summary>Reads the <c>rgb()</c> and <c>rgba()</c> functions, taking the alpha as read and discarding it.</summary>
    private static MailDocumentColour? ResolveFunction(string value)
    {
        var open = value.IndexOf('(', StringComparison.Ordinal);
        var close = value.LastIndexOf(')');

        if (open < 0 || close < open)
        {
            return null;
        }

        var arguments = value[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries);
        if (arguments.Length is not (3 or 4))
        {
            return null;
        }

        return Channels(Channel(arguments[0]), Channel(arguments[1]), Channel(arguments[2]));

        static int? Channel(string argument)
        {
            if (argument.EndsWith('%'))
            {
                return double.TryParse(
                    argument[..^1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percentage)
                    ? (int)Math.Clamp(Math.Round(percentage * 255 / 100), 0, 255)
                    : null;
            }

            return double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var channel)
                ? (int)Math.Clamp(Math.Round(channel), 0, 255)
                : null;
        }
    }

    private static MailDocumentColour? Channels(int? red, int? green, int? blue) =>
        red is { } r && green is { } g && blue is { } b
            ? new MailDocumentColour((byte)r, (byte)g, (byte)b)
            : null;
}
