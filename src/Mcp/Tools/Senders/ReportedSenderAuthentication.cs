// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Publishes what an email's author conclusion was reached from.</summary>
/// <remarks>
/// <para>
/// Only the single-email read publishes it, and it sits with the headers rather than with the verdict, because it is
/// what a reader judges the verdict by rather than what a reader acts on. A listing exists to let somebody recognize a
/// message; this is for the message they went on to open.
/// </para>
/// <para>
/// The two domains beside each other are the point. An email that authenticated as one domain while displaying another
/// is the case the whole verdict exists to make visible, and publishing both in the same normalized form is what lets a
/// client show it without parsing an address itself.
/// </para>
/// <para>
/// Every value was read back out of one header the receiving mail server wrote, whose result was recorded when the
/// email was stored. Nothing here is evaluated, re-read, or recomputed when the email is read.
/// </para>
/// </remarks>
[Description("What the author conclusion was reached from, read back from the header the receiving mail server wrote when the email arrived. Evidence for judging senderVerification rather than something to act on.")]
internal sealed record ReportedSenderAuthentication
{
    /// <summary>Gets the domain that authenticated, or <see langword="null" /> where none did.</summary>
    [Description("The domain that authenticated, which belongs to whoever handed the email over and is often a relay, a mailing list, or a delivery provider rather than the displayed author. Published in the comparison form MailFathom stores: upper-cased, and an internationalized name in its ASCII form. Null when nothing authenticated, which is an ordinary outcome and not missing data.")]
    public string? AuthenticatedDomain { get; init; }

    /// <summary>Gets the domain the email displayed as its author, or <see langword="null" /> where it wrote no usable one.</summary>
    [Description("The domain of the From header, which is what a mail client displays and what the email claims about itself. Published in the same comparison form as authenticatedDomain, so the two can be compared directly: an email that authenticated as one domain while displaying another is visible as exactly that. The conclusion drawn from that comparison is senderVerification.authorAuthentication and is not restated here. Null when the email wrote no usable From mailbox.")]
    public string? DisplayedAuthorDomain { get; init; }

    /// <summary>Gets which check established the authenticated domain, or that none did.</summary>
    [Description("Which check established authenticatedDomain: 'dkim' for a signature that verified against a key the signing domain publishes, 'spf' for an envelope sender that passed the policy the connecting address was checked against, or 'none' where nothing authenticated. DKIM is reported where both checks produced a domain, because it is the stronger claim.")]
    public required SenderAuthenticationCheck AuthenticatedBy { get; init; }

    /// <summary>Gets the DMARC result the trusted header reported.</summary>
    [Description("The DMARC result the receiving mail server reported: 'pass', 'fail', 'noPolicyPublished' when the evaluation ran and the displayed domain publishes no DMARC record, 'temporaryError' or 'permanentError' when it could not complete, and 'notReported' when the server stated no DMARC result at all. MailFathom evaluates no policy and resolves no DNS; this is read back from what the server wrote.")]
    public required DmarcResult Dmarc { get; init; }

    /// <summary>Publishes the evidence a read returned.</summary>
    /// <param name="evidence">The stored evidence to publish.</param>
    /// <returns>The wire representation of <paramref name="evidence" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="evidence" /> is <see langword="null" />.</exception>
    public static ReportedSenderAuthentication From(SenderAuthenticationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new ReportedSenderAuthentication
        {
            AuthenticatedDomain = evidence.AuthenticatedDomain?.NormalizedValue,
            DisplayedAuthorDomain = evidence.DisplayedAuthorDomain?.NormalizedValue,
            AuthenticatedBy = PublishedCheck(evidence.AuthenticatedBy),
            Dmarc = PublishedDmarcResult(evidence.Dmarc),
        };
    }

    /// <summary>Reads the published value the stored method names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored method has no published value, which means one was added to the domain without deciding what
    /// a client should be told about it.
    /// </exception>
    private static SenderAuthenticationCheck PublishedCheck(SenderAuthenticationMethod method) =>
        method switch
        {
            SenderAuthenticationMethod.None => SenderAuthenticationCheck.None,
            SenderAuthenticationMethod.DomainKeysIdentifiedMail => SenderAuthenticationCheck.Dkim,
            SenderAuthenticationMethod.SenderPolicyFramework => SenderAuthenticationCheck.Spf,
            _ => throw new ArgumentOutOfRangeException(
                nameof(method),
                method,
                "The stored sender-authentication method has no published protocol value."),
        };

    /// <summary>Reads the published value the stored DMARC outcome names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored outcome has no published value, which means one was added to the domain without deciding
    /// what a client should be told about it.
    /// </exception>
    private static DmarcResult PublishedDmarcResult(DmarcOutcome outcome) =>
        outcome switch
        {
            DmarcOutcome.NotReported => DmarcResult.NotReported,
            DmarcOutcome.Pass => DmarcResult.Pass,
            DmarcOutcome.Fail => DmarcResult.Fail,
            DmarcOutcome.NoPolicyPublished => DmarcResult.NoPolicyPublished,
            DmarcOutcome.TemporaryError => DmarcResult.TemporaryError,
            DmarcOutcome.PermanentError => DmarcResult.PermanentError,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "The stored DMARC outcome has no published protocol value."),
        };
}
