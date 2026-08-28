// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Mail;

/// <summary>Where one of a message's links goes, and what the deployment made of the way it was written.</summary>
/// <param name="Target">The absolute target, carrying only a scheme the deployment admits.</param>
/// <param name="Host">The target's host as a reader recognizes it, or <see langword="null" /> for a target that has none.</param>
/// <param name="AsciiHost">The same host in its ASCII form, present only where the two differ.</param>
/// <param name="Deception">What the link's own text said about where it goes.</param>
/// <remarks>
/// <para>
/// The target is here so the pane can show where a link goes before it is followed. It is never fetched for any other
/// purpose — no preview, no favicon, no preloading — because each of those would turn a target nobody clicked into a
/// request the sender receives.
/// </para>
/// <para>
/// <paramref name="AsciiHost" /> being present is itself the finding: it means the two forms of the host differ, which
/// is what a homograph looks like. The pane shows both wherever it is there and nothing extra where it is not.
/// </para>
/// </remarks>
public sealed record MailBodyLink(
    string Target,
    string? Host,
    string? AsciiHost,
    MailBodyLinkDeception Deception)
{
    /// <summary>Gets whether the reader is warned before this link is followed.</summary>
    /// <remarks>
    /// Three things say yes and each is enough on its own: the deployment found the text and the target disagree, the
    /// host is written in two spellings, or the verdict is one this build cannot read. The last of those errs towards
    /// warning, because a verdict that cannot be read cannot be reported as clean.
    /// </remarks>
    public bool IsWorthWarningAbout =>
        this.Deception is MailBodyLinkDeception.DisplayedHostDiffers or MailBodyLinkDeception.Unrecognized
        || this.AsciiHost is not null;

    /// <summary>Gets what the pane shows as the place this link goes.</summary>
    /// <remarks>
    /// The host where there is one, because that is what a reader judges, and the whole target otherwise — a
    /// <c>mailto</c> link has no host and its address is the useful thing to show.
    /// </remarks>
    public string Place => this.Host ?? this.Target;
}
