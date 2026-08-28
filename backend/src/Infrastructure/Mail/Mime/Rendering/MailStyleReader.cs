// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AngleSharp.Dom;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Reads what an element asked for, from the one attribute and the presentational attributes mail still uses.</summary>
/// <remarks>
/// <para>
/// The declarations are read by hand rather than through a CSS object model, and that is the point: the property set is
/// closed, so a reviewer reading this file sees the whole of what a sender can influence. A CSS engine would resolve
/// every property the specification has, and the ones this document must never admit — an offset, a transform, a float,
/// an absolute position, a stacking order — would then be a list of what to ignore rather than an absence.
/// </para>
/// <para>
/// Nothing here can place a node. What survives colours it, emphasizes it, distributes it across a width its parent
/// already decided, or hides it, and a width survives only as a share. That is what confines message style to the pane
/// it is drawn in, whatever the pane's own layout does about clipping.
/// </para>
/// <para>
/// The attribute is bounded before it is split, so a declaration block written to cost a parse costs the bound instead.
/// </para>
/// </remarks>
internal static class MailStyleReader
{
    /// <summary>The longest run of a style attribute this reads, past which the rest is left unread.</summary>
    /// <remarks>
    /// Well past the longest declaration block ordinary mail writes on one element, which runs to a few hundred
    /// characters in a templated newsletter. A body arriving with more on a single element is spending the reader's
    /// time rather than describing an appearance.
    /// </remarks>
    private const int MaximumStyleLength = 4096;

    /// <summary>Reads what one element asked for.</summary>
    /// <param name="element">The element as the message wrote it.</param>
    /// <returns>The closed property set the element resolved to.</returns>
    internal static MailNodeStyle Read(IElement element)
    {
        var style = ReadDeclarations(element);

        return style with
        {
            Hidden = style.Hidden || element.HasAttribute("hidden"),
            Foreground = style.Foreground ?? ColourAttribute(element, "color"),
            Background = style.Background ?? ColourAttribute(element, "bgcolor"),
            Alignment = style.Alignment is MailBlockAlignment.Inherited
                ? AlignmentOf(element.GetAttribute("align"))
                : style.Alignment,
            AddedEmphasis = style.AddedEmphasis | FaceEmphasis(element.GetAttribute("face")),
            WidthShare = style.WidthShare ?? ShareOf(element.GetAttribute("width")),
            PixelWidth = style.PixelWidth ?? PixelsOf(element.GetAttribute("width")),
        };
    }

    private static MailNodeStyle ReadDeclarations(IElement element)
    {
        if (element.GetAttribute("style") is not { Length: > 0 } written)
        {
            return MailNodeStyle.None;
        }

        var style = MailNodeStyle.None;

        foreach (var declaration in Bounded(written).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            style = Apply(
                style,
                declaration[..separator].Trim(),
                Declared(declaration[(separator + 1)..]));
        }

        return style;
    }

    /// <summary>Cuts an over-long attribute back to the last declaration that fits, rather than discarding them all.</summary>
    /// <remarks>
    /// The bound exists so a block written to cost a parse costs the bound instead, and it used to be answered by
    /// reading nothing — which made length the way to defeat every <see cref="MailNodeStyle.Hidden" /> check at once:
    /// a message could write <c>display:none</c> and then pad past the bound, and the element would be drawn in full
    /// while every other client hid it. Every other refusal in this file fails towards showing less, and so does this
    /// one now. The cut is taken back to the last complete declaration, because half a declaration means nothing.
    /// </remarks>
    private static string Bounded(string declarations)
    {
        if (declarations.Length <= MaximumStyleLength)
        {
            return declarations;
        }

        var read = declarations.AsSpan(0, MaximumStyleLength);
        var complete = read.LastIndexOf(';');

        return complete >= 0 ? read[..complete].ToString() : string.Empty;
    }

    /// <summary>Reads the value a declaration states, without the qualifier a sender may have written after it.</summary>
    /// <remarks>
    /// <c>display: none !important</c> is how a mail template hides preheader text and anything else meant never to be
    /// seen, so a reader comparing the whole value would leave the one form every other client honours as the one form
    /// this pane draws — and it would defeat every <see cref="MailNodeStyle.Hidden" /> check at once rather than one.
    /// The qualifier decides which declaration wins a cascade, and there is no cascade here, so it is dropped rather
    /// than carried.
    /// </remarks>
    private static string Declared(string value)
    {
        var stated = value.Trim();
        var qualifier = stated.LastIndexOf('!');

        return qualifier >= 0
            && stated.AsSpan(qualifier + 1).Trim().Equals("important", StringComparison.OrdinalIgnoreCase)
                ? stated[..qualifier].TrimEnd()
                : stated;
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "CSS property names are lower-case by specification, which is the form this switch is written in and the form a reader checks it against. The value keys a rendering decision rather than a security one.")]
    private static MailNodeStyle Apply(MailNodeStyle style, string property, string value) =>
        property.ToLowerInvariant() switch
        {
            "color" => style with { Foreground = MailCssColour.Resolve(value) ?? style.Foreground },
            "background-color" or "background" =>
                style with { Background = MailCssColour.Resolve(value) ?? style.Background },
            "text-align" => style with { Alignment = AlignmentOf(value) },
            "font-weight" => Weighted(style, value),
            "font-style" => value.StartsWith("italic", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("oblique", StringComparison.OrdinalIgnoreCase)
                    ? style with { AddedEmphasis = style.AddedEmphasis | MailTextEmphasis.Italic }
                    : style with { RemovedEmphasis = style.RemovedEmphasis | MailTextEmphasis.Italic },
            "text-decoration" or "text-decoration-line" => Decorated(style, value),
            "font-family" => style with { AddedEmphasis = style.AddedEmphasis | FaceEmphasis(value) },
            "display" => style with
            {
                Hidden = style.Hidden || value.Equals("none", StringComparison.OrdinalIgnoreCase),
            },
            "visibility" => style with
            {
                Hidden = style.Hidden || value.Equals("hidden", StringComparison.OrdinalIgnoreCase),
            },
            "width" => style with { WidthShare = ShareOf(value), PixelWidth = PixelsOf(value) },
            _ => style,
        };

    /// <summary>Reads a weight as the presence or absence of the pane's heavier face.</summary>
    /// <remarks>
    /// A numeric weight is read against 600 rather than carried, because the document has one heavier face rather than
    /// nine. Mail writes <c>font-weight: 700</c> for what it means as bold, and a scale a pane cannot draw would be a
    /// number travelling for nothing.
    /// </remarks>
    private static MailNodeStyle Weighted(MailNodeStyle style, string value)
    {
        var bold = value.StartsWith("bold", StringComparison.OrdinalIgnoreCase)
            || (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight)
                && weight >= 600);

        return bold
            ? style with { AddedEmphasis = style.AddedEmphasis | MailTextEmphasis.Bold }
            : style with { RemovedEmphasis = style.RemovedEmphasis | MailTextEmphasis.Bold };
    }

    /// <summary>Reads the two decorations the document draws, and <c>none</c> as taking both away.</summary>
    private static MailNodeStyle Decorated(MailNodeStyle style, string value)
    {
        var decorations = MailTextEmphasis.None;

        if (value.Contains("underline", StringComparison.OrdinalIgnoreCase))
        {
            decorations |= MailTextEmphasis.Underline;
        }

        if (value.Contains("line-through", StringComparison.OrdinalIgnoreCase))
        {
            decorations |= MailTextEmphasis.Strikethrough;
        }

        return value.Contains("none", StringComparison.OrdinalIgnoreCase)
            ? style with
            {
                RemovedEmphasis = style.RemovedEmphasis
                    | MailTextEmphasis.Underline
                    | MailTextEmphasis.Strikethrough,
            }
            : style with { AddedEmphasis = style.AddedEmphasis | decorations };
    }

    /// <summary>Reads a face as whether the pane's fixed-width one was asked for.</summary>
    /// <remarks>
    /// The four names below are what mail writes when it means a terminal or a code sample. Every other face is dropped:
    /// a message cannot choose the face somebody reads their mail in, because a face that is not installed is a
    /// substitution the sender never saw and a web font is a request to somebody else's server.
    /// </remarks>
    private static MailTextEmphasis FaceEmphasis(string? face) =>
        face is not null
        && (face.Contains("monospace", StringComparison.OrdinalIgnoreCase)
            || face.Contains("courier", StringComparison.OrdinalIgnoreCase)
            || face.Contains("consolas", StringComparison.OrdinalIgnoreCase)
            || face.Contains("menlo", StringComparison.OrdinalIgnoreCase))
            ? MailTextEmphasis.Monospace
            : MailTextEmphasis.None;

    private static MailDocumentColour? ColourAttribute(IElement element, string name) =>
        MailCssColour.Resolve(element.GetAttribute(name));

    private static MailBlockAlignment AlignmentOf(string? value)
    {
        var written = value?.Trim();

        if (Names(written, "left", "start"))
        {
            return MailBlockAlignment.Start;
        }

        if (Names(written, "center", "centre", "middle"))
        {
            return MailBlockAlignment.Center;
        }

        if (Names(written, "right", "end"))
        {
            return MailBlockAlignment.End;
        }

        return Names(written, "justify") ? MailBlockAlignment.Justify : MailBlockAlignment.Inherited;
    }

    private static bool Names(string? value, params string[] names) =>
        value is not null && names.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a percentage width as the share of the parent it is.</summary>
    private static double? ShareOf(string? value)
    {
        if (value is null || !value.EndsWith('%'))
        {
            return null;
        }

        return double.TryParse(
            value[..^1].Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var percentage)
            && double.IsFinite(percentage)
            && percentage is > 0 and <= 100
            ? percentage / 100
            : null;
    }

    /// <summary>Reads a pixel width, which is never a share on its own and is only ever resolved against siblings.</summary>
    /// <remarks>
    /// The finiteness check is the whole reason this refuses rather than clamps. <c>double.TryParse</c> answers an
    /// overflowing literal with <see cref="double.PositiveInfinity" /> rather than failing, so <c>width="1e400px"</c>
    /// would arrive as a real width, be summed into an infinite total, and resolve to a share of <c>NaN</c> — which
    /// <c>Utf8JsonWriter</c> refuses to write, so one attribute a stranger wrote would cost the reader the whole
    /// message rather than one column's proportion.
    /// </remarks>
    private static double? PixelsOf(string? value)
    {
        if (value is null || value.EndsWith('%'))
        {
            return null;
        }

        var digits = value.Trim();
        if (digits.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[..^2].Trim();
        }

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
            && double.IsFinite(pixels)
            && pixels > 0
            ? pixels
            : null;
    }
}
