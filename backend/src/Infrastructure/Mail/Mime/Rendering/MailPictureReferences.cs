// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using AngleSharp.Dom;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Names the pictures a parsed body asks for, so only those are decoded out of the message.</summary>
/// <remarks>
/// <para>
/// The resolution of a message's own parts is bounded by how much they come to together, and that bound is what the
/// answer is sized by. Deciding which parts to spend it on by walking the message rather than the body spends it on
/// whatever the sender attached first: an attached photograph carrying a content identifier is indistinguishable from
/// an inline logo until the body is read, and the picture the reader would actually have seen is the one that is lost.
/// </para>
/// <para>
/// The references are read from the parsed body rather than from the markup, so a source inside a construct the
/// reduction drops is still named here. That is deliberate and conservative: naming one picture too many costs the
/// budget a decode, while naming one too few costs the reader a picture the message carried.
/// </para>
/// </remarks>
internal static class MailPictureReferences
{
    /// <summary>Reads every reference the body's pictures name, in the form a part is keyed by.</summary>
    /// <param name="body">The parsed body element.</param>
    /// <returns>The references, with nothing repeated.</returns>
    internal static IReadOnlySet<string> NamedBy(IElement body)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var picture in body.QuerySelectorAll("img"))
        {
            if (picture.GetAttribute("src") is not { Length: > 0 } source)
            {
                continue;
            }

            var reference = source.Trim();

            named.Add(MailInlineImages.KeyOf(reference));

            // The reduction resolves an absolute reference in its normalized form, which is not always the form the
            // message wrote — so both are named rather than the parse being trusted to have left the string alone.
            if (Uri.TryCreate(reference, UriKind.Absolute, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                named.Add(MailInlineImages.KeyOf(absolute.AbsoluteUri));
            }
        }

        return named;
    }
}
