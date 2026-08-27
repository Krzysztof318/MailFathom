// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>The pictures a message carries in itself, resolved into what a pane can draw with no second request.</summary>
/// <remarks>
/// <para>
/// A <c>cid:</c> reference points at a part of the same message, so resolving one is a lookup rather than a fetch. The
/// resolution happens here, before the body is walked, and what the document carries is the picture itself — which is
/// what makes the default path need no request-interception API on any head, including the browser head where Uno
/// documents that there is none.
/// </para>
/// <para>
/// This is the one place a message's own octets are held in memory during a rendering, and it is bounded twice: how
/// many pictures are resolved at all, and how large one may be. A part past either is reported as a picture the message
/// carries and the pane does not draw, never as a reference for something to resolve later.
/// </para>
/// <para>
/// The media types are an allow-list, and <c>image/svg+xml</c> is deliberately absent from it. An SVG is a document
/// that can carry script and can reference somebody else's server, so inlining one would hand a renderer exactly what
/// this whole path exists to keep away from it.
/// </para>
/// </remarks>
internal sealed class MailInlineImages
{
    private static readonly string[] DrawableSubtypes =
    [
        "png", "jpeg", "jpg", "gif", "webp", "bmp",
    ];

    private readonly Dictionary<string, string> byReference;

    private MailInlineImages(Dictionary<string, string> byReference, int resolved, int undrawn)
    {
        this.byReference = byReference;
        this.ResolvedCount = resolved;
        this.UndrawnCount = undrawn;
    }

    /// <summary>Gets how many of the message's own pictures were resolved.</summary>
    public int ResolvedCount { get; }

    /// <summary>Gets how many of the message's own pictures were left undrawn because they were beyond a bound.</summary>
    public int UndrawnCount { get; }

    /// <summary>Gets the resolution of a message that carries no picture of its own.</summary>
    public static MailInlineImages None { get; } = new([], resolved: 0, undrawn: 0);

    /// <summary>Resolves every picture the message carries in itself.</summary>
    /// <param name="message">The parsed message.</param>
    /// <param name="maximumImages">How many pictures may be resolved at all.</param>
    /// <param name="maximumOctets">How large one picture may be.</param>
    /// <param name="cancellationToken">Cancels the decode.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    public static async Task<MailInlineImages> ResolveAsync(
        MimeMessage message,
        int maximumImages,
        int maximumOctets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var byReference = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var undrawn = 0;

        foreach (var part in message.BodyParts.OfType<MimePart>().Where(IsDrawable))
        {
            var references = ReferencesOf(part).ToArray();
            if (references.Length == 0 || references.All(byReference.ContainsKey))
            {
                continue;
            }

            if (byReference.Count >= maximumImages)
            {
                undrawn++;

                continue;
            }

            var inlined = await InlineAsync(part, maximumOctets, cancellationToken);
            if (inlined is null)
            {
                undrawn++;

                continue;
            }

            foreach (var reference in references)
            {
                byReference[reference] = inlined;
            }
        }

        return new MailInlineImages(byReference, byReference.Values.Distinct(StringComparer.Ordinal).Count(), undrawn);
    }

    /// <summary>Resolves one reference the body wrote.</summary>
    /// <param name="reference">The reference as the body wrote it, with or without its scheme.</param>
    /// <returns>The picture as a <c>data:</c> URI, or <see langword="null" /> where the message carries no such part.</returns>
    public string? Resolve(string reference)
    {
        var identifier = reference.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)
            ? reference[4..]
            : reference;

        return this.byReference.GetValueOrDefault(Uri.UnescapeDataString(identifier.Trim().Trim('<', '>')));
    }

    /// <summary>Answers whether a part is a picture this resolution draws.</summary>
    /// <remarks>
    /// A part is a candidate because of what it is rather than because of how it was dispositioned: mail routinely
    /// attaches its own logo with no <c>inline</c> disposition and references it by content identifier anyway, and a
    /// picture with no reference into the body is never looked up here in the first place.
    /// </remarks>
    private static bool IsDrawable(MimePart part) =>
        part.ContentType.MediaType is not null
        && part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)
        && part.ContentType.MediaSubtype is { } subtype
        && DrawableSubtypes.Contains(subtype, StringComparer.OrdinalIgnoreCase);

    /// <summary>Names every way the body could refer to one part.</summary>
    private static IEnumerable<string> ReferencesOf(MimePart part)
    {
        if (part.ContentId is { Length: > 0 } contentId)
        {
            yield return contentId.Trim().Trim('<', '>');
        }

        if (part.ContentLocation is { IsAbsoluteUri: true } location)
        {
            yield return location.AbsoluteUri;
        }
    }

    /// <summary>Decodes one part into the <c>data:</c> URI the document carries, or reports that it is beyond the bound.</summary>
    private static async Task<string?> InlineAsync(
        MimePart part,
        int maximumOctets,
        CancellationToken cancellationToken)
    {
        if (part.Content is not { } content)
        {
            return null;
        }

        await using var buffer = new BoundedContentBuffer(maximumOctets);

        await content.DecodeToAsync(buffer, cancellationToken);

        if (buffer.ExceededBound || buffer.Length == 0)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(buffer.Kept().Span)}");
    }
}
