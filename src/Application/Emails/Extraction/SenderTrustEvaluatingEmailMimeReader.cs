// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Holds the author a message authenticated as against what the receiving account recognizes.</summary>
/// <remarks>
/// <para>
/// A decorator rather than a step inside the parsing adapter, because the two halves answer different questions: what
/// the receiving server established is read out of the message's bytes and is true of the message forever, while whether
/// this deployment recognizes the author it established is a decision about a list an operator and a reader both write
/// to. Keeping the decision above the parser is what leaves the parser with no opinion to hold.
/// </para>
/// <para>
/// It sits at this seam for the reason <see cref="RedactingEmailMimeReader" /> does: both paths that produce a reading
/// — synchronization reading a message it has just fetched, and the backfill re-reading raw MIME stored earlier — reach
/// it through this port, so the verdict is written wherever a reading is and re-derived wherever one is re-derived.
/// </para>
/// <para>
/// The verdict is stored rather than recomputed on read, so a later change to a list does not silently rewrite what a
/// reader was already shown. What re-judges mail already stored is the extraction backfill, which is the same
/// deliberate act that re-reads it after an account gained a trusted authority.
/// </para>
/// </remarks>
public sealed class SenderTrustEvaluatingEmailMimeReader : IEmailMimeReader
{
    private readonly IEmailMimeReader inner;
    private readonly ISenderTrustPolicyReader policies;

    /// <summary>Initializes a reader that judges the author the one it wraps established.</summary>
    /// <param name="inner">The reader that turns raw MIME into normalized metadata.</param>
    /// <param name="policies">Resolves the trusted senders an account judges its mail by.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SenderTrustEvaluatingEmailMimeReader(IEmailMimeReader inner, ISenderTrustPolicyReader policies)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policies);

        this.inner = inner;
        this.policies = policies;
    }

    /// <inheritdoc />
    public async Task<EmailMimeExtractionResult> ReadMetadataAsync(
        RemoteEmailContent content,
        CancellationToken cancellationToken)
    {
        var extraction = await this.inner.ReadMetadataAsync(content, cancellationToken);

        // A message nobody could parse establishes no author to judge, and reaches storage with the columns the envelope
        // alone supports — which is the unknown answer, and the same one it already carries.
        if (extraction.Metadata is not { } metadata)
        {
            return extraction;
        }

        var policy = this.policies.GetTrustPolicy(metadata.OccurrenceId.AccountId);

        return EmailMimeExtractionResult.Extracted(metadata with
        {
            SenderTrust = policy.Evaluate(metadata.SenderAuthentication, DisplayedSenderOf(metadata)),
        });
    }

    /// <summary>Reads the address a mail client displays as the message's author.</summary>
    /// <remarks>
    /// Taken from <c>From</c> alone and never from <c>Sender</c>, which is the same choice the author identity itself
    /// makes and for the same reason: an address entry is a claim about the mailbox a reader sees, and <c>Sender</c>
    /// names whoever submitted a message written on somebody else's behalf. The first mailbox wins where the header
    /// carried several, because a message can display only one author.
    /// </remarks>
    private static EmailAddress? DisplayedSenderOf(ExtractedEmailMetadata metadata) =>
        metadata.Participants.FirstOrDefault(participant => participant.Role == EmailAddressRole.From)?.Address;
}
