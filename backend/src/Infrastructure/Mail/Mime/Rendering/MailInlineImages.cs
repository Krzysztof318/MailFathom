// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>The pictures a message carries in itself, resolved into what a pane can draw with no second request.</summary>
/// <remarks>
/// <para>
/// A <c>cid:</c> reference points at a part of the same message, so resolving one is a lookup rather than a fetch. The
/// resolution happens here, before the body is walked, and what the document carries is the picture itself — which is
/// what makes the default path need no request-interception API in any client, including a browser-hosted one where
/// there may be none to reach for.
/// </para>
/// <para>
/// This is the one place a message's own octets are held in memory during a rendering, and it is bounded three times:
/// how many pictures are resolved at all, how large one may be, and how much they may come to together — the last of
/// those being the bound the answer is sized by rather than the message. A part past any of them is reported as a
/// picture the message carries and the pane does not draw, never as a reference for something to resolve later.
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

    /// <summary>Reads how many characters of a serialization are the pictures this resolution put into it.</summary>
    /// <param name="serialized">The serialized markup to read.</param>
    /// <returns>The characters those pictures occupy, counting every occurrence, because each one is in the string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serialized" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// What a representation bounded in characters has to discount is what it inlined, and this resolution is the only
    /// thing that knows what that was. Reading the answer out of the string instead — by matching whatever looks like a
    /// <c>data:</c> URI — would discount a sender's own words the moment they happened to be shaped like one, which is
    /// a bound a message could talk its way out of.
    /// </para>
    /// <para>
    /// Occurrences rather than pictures, because a character bound is about how long the string is: one picture named
    /// twice is written twice. The count is over a handful of entries, so it is a scan of the serialization per picture
    /// rather than a parse of it.
    /// </para>
    /// </remarks>
    public long CharactersOccupiedIn(string serialized)
    {
        ArgumentNullException.ThrowIfNull(serialized);

        return this.byReference.Values
            .Distinct(StringComparer.Ordinal)
            .Sum(picture => (long)OccurrencesOf(serialized, picture) * picture.Length);
    }

    /// <summary>Resolves the pictures the body asks for out of the message's own parts.</summary>
    /// <param name="message">The parsed message.</param>
    /// <param name="named">The references the body's pictures name, which is what the budget is spent on.</param>
    /// <param name="maximumImages">How many pictures may be resolved at all.</param>
    /// <param name="maximumOctets">How large one picture may be.</param>
    /// <param name="maximumOctetsInTotal">How many octets every resolved picture may come to together.</param>
    /// <param name="cancellationToken">Cancels the decode.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A part the body never names is not decoded and is not reported as undrawn either, because nothing was going to
    /// draw it: an attachment carrying a content identifier is a file rather than a picture in the message, and
    /// spending the document's octet budget on it is what would leave the logo the body does draw refused.
    /// </para>
    /// <para>
    /// The count is of pictures rather than of the references that reach them, which is why a part already inlined
    /// under another reference costs nothing here: a message naming one photograph by both its content identifier and
    /// its location is carrying one picture, and counting it twice would leave a reader a photograph short of the
    /// bound they were promised.
    /// </para>
    /// </remarks>
    public static async Task<MailInlineImages> ResolveAsync(
        MimeMessage message,
        IReadOnlySet<string> named,
        int maximumImages,
        int maximumOctets,
        int maximumOctetsInTotal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(named);

        var byReference = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolved = 0;
        var octets = 0L;
        var undrawn = 0;

        foreach (var part in message.BodyParts.OfType<MimePart>().Where(IsDrawable))
        {
            var references = ReferencesOf(part).ToArray();
            if (references.Length == 0
                || references.All(byReference.ContainsKey)
                || !references.Any(named.Contains))
            {
                continue;
            }

            if (resolved >= maximumImages)
            {
                undrawn++;

                continue;
            }

            // The per-picture bound is narrowed to what the document has left, so a decode stops at the aggregate
            // rather than completing and being discarded.
            var remaining = maximumOctetsInTotal - octets;
            var inlined = remaining <= 0
                ? null
                : await InlineAsync(part, (int)Math.Min(maximumOctets, remaining), cancellationToken);

            if (inlined is null)
            {
                undrawn++;

                continue;
            }

            resolved++;
            octets += OctetsBehind(inlined);

            foreach (var reference in references)
            {
                byReference[reference] = inlined;
            }
        }

        return new MailInlineImages(byReference, resolved, undrawn);
    }

    /// <summary>Counts how many times one picture was written into a serialization.</summary>
    /// <remarks>
    /// The step advances by the picture's own length rather than by one, because a <c>data:</c> URI cannot overlap
    /// itself and stepping by one would rescan every character of a megabyte-long reference.
    /// </remarks>
    private static int OccurrencesOf(string serialized, string picture)
    {
        var occurrences = 0;

        for (var at = serialized.IndexOf(picture, StringComparison.Ordinal);
            at >= 0;
            at = serialized.IndexOf(picture, at + picture.Length, StringComparison.Ordinal))
        {
            occurrences++;
        }

        return occurrences;
    }

    /// <summary>Reads how many octets a composed <c>data:</c> URI carries, which is what the aggregate counts.</summary>
    private static long OctetsBehind(string inlined) =>
        (long)(inlined.Length - inlined.IndexOf(',', StringComparison.Ordinal) - 1) * 3 / 4;

    /// <summary>Resolves one reference the body wrote.</summary>
    /// <param name="reference">The reference as the body wrote it, with or without its scheme.</param>
    /// <returns>The picture as a <c>data:</c> URI, or <see langword="null" /> where the message carries no such part.</returns>
    public string? Resolve(string reference) => this.byReference.GetValueOrDefault(KeyOf(reference));

    /// <summary>Reads the form a part is keyed by, out of a reference however the body wrote it.</summary>
    /// <param name="reference">The reference as the body wrote it, with or without its scheme.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// One place rather than two, because the walk that decides which parts to decode and the walk that looks one up
    /// have to agree: a reference normalized differently by the two would resolve to a part nothing decoded.
    /// </remarks>
    internal static string KeyOf(string reference)
    {
        var identifier = reference.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)
            ? reference[4..]
            : reference;

        return Uri.UnescapeDataString(identifier.Trim().Trim('<', '>'));
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

    /// <summary>Names every way the body could refer to one part, in the form a lookup is keyed by.</summary>
    /// <remarks>
    /// Through <see cref="KeyOf" /> rather than as the part wrote them, so what is stored and what is looked up are
    /// normalized by one function. Two normalizations would resolve a reference the message and the body spelled
    /// differently to nothing at all.
    /// </remarks>
    private static IEnumerable<string> ReferencesOf(MimePart part)
    {
        if (part.ContentId is { Length: > 0 } contentId)
        {
            yield return KeyOf(contentId);
        }

        if (part.ContentLocation is { IsAbsoluteUri: true } location)
        {
            yield return KeyOf(location.AbsoluteUri);
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
