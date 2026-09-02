// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authorship;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Judges how much a message's own text reads as machine written, from the text the parse already produced.</summary>
/// <remarks>
/// <para>
/// A decorator rather than a step inside the parsing adapter, for the reason
/// <see cref="SenderTrustEvaluatingEmailMimeReader" /> is one: what the message's bytes say is read out of the message,
/// while what a combination of signals is worth is a judgement this deployment makes and may make differently later.
/// Keeping the judgement above the parser leaves the parser with no opinion to hold.
/// </para>
/// <para>
/// It sits at this seam so that both paths that produce a reading — synchronization reading a message it has just
/// fetched, and the backfill re-reading raw MIME stored earlier — reach it through this port. The assessment is
/// therefore written wherever a reading is, and re-derived wherever one is re-derived.
/// </para>
/// <para>
/// It sits <em>below</em> redaction deliberately. Redaction replaces the words a scanner recognized as sensitive, and
/// reading the text afterwards would judge a message partly by what a scanner rewrote in it. Reading it first is safe
/// because nothing about the text leaves this step: what comes out is a signal set, a number, and a band, none of which
/// can carry a fragment of the message.
/// </para>
/// </remarks>
public sealed class MachineAuthorshipEvaluatingEmailMimeReader : IEmailMimeReader
{
    private readonly IEmailMimeReader inner;
    private readonly MachineAuthorshipProfile profile;

    /// <summary>Initializes a reader that assesses the text the one it wraps extracted.</summary>
    /// <param name="inner">The reader that turns raw MIME into normalized metadata.</param>
    /// <param name="profile">What each signal is worth, or the profile of a deployment that assesses nothing.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MachineAuthorshipEvaluatingEmailMimeReader(IEmailMimeReader inner, MachineAuthorshipProfile profile)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(profile);

        this.inner = inner;
        this.profile = profile;
    }

    /// <inheritdoc />
    public async Task<EmailMimeExtractionResult> ReadMetadataAsync(
        RemoteEmailContent content,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var extraction = await this.inner.ReadMetadataAsync(content, owner, cancellationToken);

        // A message nobody could parse yielded no text to read, and reaches storage carrying the not-assessed state it
        // already holds — which is the same state a message with an empty body reaches by being read.
        if (extraction.Metadata is not { } metadata)
        {
            return extraction;
        }

        return EmailMimeExtractionResult.Extracted(metadata with
        {
            MachineAuthorship = this.profile.Assess(metadata.Text.OriginalText, metadata.Text.TrimmedText),
        });
    }
}
