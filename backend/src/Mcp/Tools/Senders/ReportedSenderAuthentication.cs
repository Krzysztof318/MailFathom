// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// The two domains are published beside the verdict rather than against each other. An email relayed by a provider that
/// signs as itself, while the envelope sender passes for the author's own domain, authenticates exactly as it appears
/// and still names two different domains here, because the authenticated one is whichever identity authenticated the
/// transport. Publishing both in the same normalized form is what lets a client show what stood behind an email; what
/// says its displayed author was not established is the verdict.
/// </para>
/// <para>
/// One of two readings produced these values and <see cref="VerdictSource" /> says which: the header the receiving mail
/// server wrote, or MailFathom's own verification of the email's DKIM signatures where no such header was available.
/// Either way the result was recorded when the email was stored, and nothing here is evaluated, re-read, resolved, or
/// recomputed when the email is read.
/// </para>
/// </remarks>
[Description("What the author conclusion was reached from, recorded when the email arrived. verdictSource says whether it came from the receiving mail server's header or from MailFathom verifying the email's own DKIM signatures. Evidence for judging senderVerification rather than something to act on.")]
internal sealed record ReportedSenderAuthentication
{
    /// <summary>Gets the domain that authenticated, or <see langword="null" /> where none did.</summary>
    [Description("The domain that authenticated, which belongs to whoever handed the email over and is often a relay, a mailing list, or a delivery provider rather than the displayed author. Published in the comparison form MailFathom stores: upper-cased, and an internationalized name in its ASCII form. Null when nothing authenticated, which is an ordinary outcome and not missing data.")]
    public string? AuthenticatedDomain { get; init; }

    /// <summary>Gets the domain the email displayed as its author, or <see langword="null" /> where it wrote no usable one.</summary>
    [Description("The domain of the From header, which is what a mail client displays and what the email claims about itself. Published in the same comparison form as authenticatedDomain. Do not read a difference between the two as impersonation: authenticatedDomain is whichever identity authenticated the transport, so an email sent through a provider that signs as itself while spf passes for the author's own domain differs here and is authenticated exactly as it appears. senderVerification.authorAuthentication is what says whether the displayed author was established. Null when the email wrote no usable From mailbox.")]
    public string? DisplayedAuthorDomain { get; init; }

    /// <summary>Gets which check established the authenticated domain, or that none did.</summary>
    [Description("Which check established authenticatedDomain: 'dkim' for a signature that verified against a key the signing domain publishes, 'spf' for an envelope sender that passed the policy the connecting address was checked against, or 'none' where nothing authenticated. DKIM is reported where both checks produced a domain, because it is the stronger claim.")]
    public required SenderAuthenticationCheck AuthenticatedBy { get; init; }

    /// <summary>Gets the DMARC result the trusted header reported.</summary>
    [Description("The DMARC result the receiving mail server reported: 'pass', 'fail', 'noPolicyPublished' when the evaluation ran and the displayed domain publishes no DMARC record, 'temporaryError' or 'permanentError' when it could not complete, and 'notReported' when the server stated no DMARC result at all. Always 'notReported' when verdictSource is 'localVerification', because reporting a DMARC result needs the displayed domain's published policy and MailFathom resolves none.")]
    public required DmarcResult Dmarc { get; init; }

    /// <summary>Gets who reached the verdict this is the evidence for.</summary>
    [Description("Who reached the verdict: 'receivingServer' when it was read back from the Authentication-Results header the receiving mail server wrote, or 'localVerification' when MailFathom verified the email's own DKIM signatures itself because no trusted server statement was available. A server observed the connection the email arrived on and could evaluate spf and dmarc against it; local verification has the signed bytes and a published key only, so on such a verdict authenticatedBy is never 'spf' and dmarc is never anything but 'notReported'. Neither absence is a finding about the email.")]
    public required SenderVerdictSource VerdictSource { get; init; }

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
            VerdictSource = PublishedVerdictSource(evidence.Source),
        };
    }

    /// <summary>Reads the published value the stored verdict source names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored source has no published value, which means one was added to the domain without deciding
    /// what a client should be told about it.
    /// </exception>
    private static SenderVerdictSource PublishedVerdictSource(SenderAuthenticationSource source) =>
        source switch
        {
            SenderAuthenticationSource.ReceivingServer => SenderVerdictSource.ReceivingServer,
            SenderAuthenticationSource.LocalVerification => SenderVerdictSource.LocalVerification,
            _ => throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "The stored sender-authentication source has no published protocol value."),
        };

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
