// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Turns one of a message's links into where it goes and what its own text claimed about that.</summary>
/// <remarks>
/// <para>
/// The determination is made once, in the service, and travels beside the link. Two renderers deriving it for
/// themselves would be two chances to derive it differently, and a second client could then be quieter about a
/// deceptive link than the first — which is the failure this placement exists to rule out.
/// </para>
/// <para>
/// Three schemes survive and no others. A relative reference is dropped rather than completed, because there is no base
/// to complete it against that would mean anything: the message was written for a mail client, not served from a site,
/// so a resolved relative reference would point at whatever host somebody supplied as a base.
/// </para>
/// <para>
/// Nothing here resolves the address over the network. It is parsed, its host is read in both forms, and it is carried
/// so a reader can be shown where the link goes before following it.
/// </para>
/// </remarks>
internal static class MailLinkReader
{
    /// <summary>The longest target this carries, past which a link is a payload rather than a place.</summary>
    private const int MaximumTargetLength = 4096;

    /// <summary>The longest link text this reads a host out of.</summary>
    /// <remarks>
    /// A link whose text is a paragraph is a link whose text is not a place, so there is nothing to compare and no
    /// reason to spend a parse establishing that.
    /// </remarks>
    private const int MaximumComparedTextLength = 512;

    private static readonly IdnMapping Idn = new();

    /// <summary>Reads one link.</summary>
    /// <param name="href">The reference as the message wrote it.</param>
    /// <param name="displayText">The words the link shows, which is what its target is judged against.</param>
    /// <returns>The link, or <see langword="null" /> where the reference is not one a reader may follow.</returns>
    internal static MailDocumentLink? Read(string? href, string displayText)
    {
        if (href is null)
        {
            return null;
        }

        var reference = href.Trim();
        if (reference.Length is 0 or > MaximumTargetLength)
        {
            return null;
        }

        if (!Uri.TryCreate(reference, UriKind.Absolute, out var target) || !IsFollowable(target))
        {
            return null;
        }

        if (target.AbsoluteUri.Length > MaximumTargetLength)
        {
            return null;
        }

        if (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
        {
            return new MailDocumentLink(
                target.AbsoluteUri,
                Host: null,
                AsciiHost: null,
                MailLinkDeception.NotApplicable);
        }

        var asciiHost = AsciiHostOf(target);
        var host = UnicodeHostOf(asciiHost);

        return new MailDocumentLink(
            target.AbsoluteUri,
            host,
            string.Equals(host, asciiHost, StringComparison.Ordinal) ? null : asciiHost,
            DeceptionOf(displayText, asciiHost));
    }

    /// <summary>Answers whether a scheme is one a reader may be handed.</summary>
    /// <remarks>
    /// An allow-list rather than a list of what to refuse. <c>javascript:</c> and <c>data:</c> are the two everyone
    /// names, and naming them is exactly the mistake — the schemes a platform opener will act on are decided by the
    /// operating system rather than by this file, so what a link may carry has to be the set somebody chose.
    /// </remarks>
    private static bool IsFollowable(Uri target) =>
        target.Scheme == Uri.UriSchemeHttp
        || target.Scheme == Uri.UriSchemeHttps
        || target.Scheme == Uri.UriSchemeMailto;

    private static string AsciiHostOf(Uri target)
    {
        try
        {
            // System.Uri normalizes the host component to lower case while it parses, so nothing here has to.
            return target.IdnHost;
        }
        catch (ArgumentException)
        {
            // A host the IDN mapping refuses is reported as it was written, so a reader is still shown something
            // rather than a link with no place attached to it.
            return target.Host;
        }
    }

    /// <summary>Reads the host as a person recognizes it, which is what makes a homograph visible beside its ASCII form.</summary>
    private static string UnicodeHostOf(string asciiHost)
    {
        try
        {
            return Idn.GetUnicode(asciiHost);
        }
        catch (ArgumentException)
        {
            return asciiHost;
        }
    }

    /// <summary>Judges the link's own text against the host it actually goes to.</summary>
    /// <remarks>
    /// Only text that names a place is judged. A link whose words are a sentence claims nothing about where it goes, so
    /// reporting it as honest would be as wrong as reporting it as deceptive — which is why the contract has a value
    /// for having nothing to compare rather than folding that into the honest one.
    /// </remarks>
    private static MailLinkDeception DeceptionOf(string displayText, string asciiHost)
    {
        if (HostNamedBy(displayText) is not { } named)
        {
            return MailLinkDeception.NotApplicable;
        }

        return string.Equals(Registrable(named), Registrable(asciiHost), StringComparison.Ordinal)
            ? MailLinkDeception.None
            : MailLinkDeception.DisplayedHostDiffers;
    }

    /// <summary>Reads the host a link's text names, or reports that it names none.</summary>
    private static string? HostNamedBy(string displayText)
    {
        var text = displayText.Trim();
        if (text.Length is 0 or > MaximumComparedTextLength || text.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var written)
            && (written.Scheme == Uri.UriSchemeHttp || written.Scheme == Uri.UriSchemeHttps))
        {
            return AsciiHostOf(written);
        }

        // Text written as a bare authority, which is how a phishing message spells a bank. It is judged only when it
        // reads as a host and nothing else: an address carries an at sign, and a word carries no dot.
        var authority = text.Split('/', 2)[0];

        return !authority.Contains('@', StringComparison.Ordinal)
            && authority.Contains('.', StringComparison.Ordinal)
            && Uri.TryCreate($"http://{authority}", UriKind.Absolute, out var asHost)
            && string.Equals(asHost.Authority, authority, StringComparison.OrdinalIgnoreCase)
                ? AsciiHostOf(asHost)
                : null;
    }

    /// <summary>Drops the one label that is never what a reader means by "somewhere else".</summary>
    /// <remarks>
    /// A message writing <c>example.com</c> over a link to <c>www.example.com</c> is not deceiving anybody, and
    /// reporting it as a mismatch would spend the warning on the ordinary case until nobody read it. Nothing further is
    /// stripped: a subdomain genuinely is a different place, and public-suffix reasoning would make
    /// <c>evil.example.co.uk</c> and <c>example.co.uk</c> the same host.
    /// </remarks>
    private static string Registrable(string host) =>
        host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
}
