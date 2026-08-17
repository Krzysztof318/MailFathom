// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>What a stored email's authentication verdict was reached from, read back as it was stored.</summary>
/// <remarks>
/// <para>
/// It is how a reader judges <see cref="SenderVerification.AuthorAuthentication" /> rather than what a reader acts on,
/// which is why it travels apart from the pair and why only the single-email read publishes it. The conclusion is the
/// verdict's and is not restated here.
/// </para>
/// <para>
/// The two domains are read beside that verdict rather than against each other. <see cref="AuthenticatedDomain" /> is
/// the identity that authenticated the transport, and where both checks produced one it is the DKIM domain — which need
/// not be the identity that established the author, since an SPF domain matching the displayed one establishes it just
/// as well. So a message relayed by a provider that signs as itself while SPF matches the author's own domain publishes
/// two different domains and is authenticated exactly as it appears. What says the displayed author was not established
/// is <see cref="SenderVerification.AuthorAuthentication" />, never a difference between these two.
/// </para>
/// <para>
/// Every absent value is an ordinary outcome rather than missing data. A message nothing authenticated names no
/// authenticated domain, a message displaying no usable <c>From</c> mailbox names no displayed one, and a server that
/// evaluated no DMARC policy reports that it did not.
/// </para>
/// <para>
/// Each domain is personal data and inherits the classification of the mail it was read from.
/// </para>
/// </remarks>
public sealed record SenderAuthenticationEvidence
{
    /// <summary>Gets the evidence a row carries where nothing trusted was read for the message.</summary>
    public static SenderAuthenticationEvidence None { get; } = new()
    {
        AuthenticatedBy = SenderAuthenticationMethod.None,
        Dmarc = DmarcOutcome.NotReported,
    };

    /// <summary>Gets the domain that authenticated, or <see langword="null" /> where none did.</summary>
    public SenderDomain? AuthenticatedDomain { get; init; }

    /// <summary>Gets the domain the message displayed in <c>From</c>, or <see langword="null" /> where it wrote no usable one.</summary>
    /// <remarks>
    /// The <c>From</c> header alone, never the <c>Sender</c> fallback a timeline names a message's sender by, because
    /// the author a mail client displays is what an impersonation gets wrong. It is recorded and never believed.
    /// </remarks>
    public SenderDomain? DisplayedAuthorDomain { get; init; }

    /// <summary>Gets which check established <see cref="AuthenticatedDomain" />, or that none did.</summary>
    public required SenderAuthenticationMethod AuthenticatedBy { get; init; }

    /// <summary>Gets the DMARC result the trusted header reported, or that it reported none.</summary>
    public required DmarcOutcome Dmarc { get; init; }
}
