// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Where one of a message's links goes, and what the service made of the way it was written.</summary>
/// <param name="Target">The absolute target, resolved and carrying only a scheme this contract admits.</param>
/// <param name="Host">The target's host as a reader recognizes it, or <see langword="null" /> for a target that has none.</param>
/// <param name="AsciiHost">The same host in its ASCII form, present only where the two differ.</param>
/// <param name="Deception">What the link's own text said about where it goes.</param>
/// <remarks>
/// <para>
/// A target is the one sender-controlled absolute address the default path carries on purpose, and the reason is the
/// whole of this type: a reader has to be shown where a link goes before following it, which needs the address. It is
/// never resolved for any other purpose — no preview, no favicon, no preloading — because each of those would turn a
/// target nobody clicked into a request the sender receives.
/// </para>
/// <para>
/// <paramref name="AsciiHost" /> is present exactly when the host's Unicode and ASCII forms differ, which is what makes
/// a homograph visible as one rather than as an ordinary name. A client shows both wherever it is present and nothing
/// extra where it is not.
/// </para>
/// <para>
/// Only <c>http</c>, <c>https</c>, and <c>mailto</c> reach here. A link the reduction could not resolve to one of those
/// leaves its text behind as ordinary words rather than as something a reader could follow.
/// </para>
/// </remarks>
public sealed record MailDocumentLink(
    string Target,
    string? Host,
    string? AsciiHost,
    MailLinkDeception Deception)
{
    /// <summary>Gets whether the reader should be warned before following this link.</summary>
    /// <remarks>
    /// Stated rather than left to each renderer to derive from the members, so the pane and anything else drawing a
    /// link cannot come to disagree about which links are worth a warning.
    /// </remarks>
    public bool IsWorthWarningAbout =>
        this.Deception is MailLinkDeception.DisplayedHostDiffers || this.AsciiHost is not null;
}
