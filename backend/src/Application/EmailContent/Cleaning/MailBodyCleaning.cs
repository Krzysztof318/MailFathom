// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>Draws one message as the third rendering: the reduced document with what the sender wrapped it in dropped.</summary>
/// <remarks>
/// <para>
/// The pass exists because the reduction is deterministic and the remainder is not. What survives a reduction and is
/// still not the message — a preheader written for an inbox preview, a category menu, a row of social icons, a pasted
/// header block, a postal address, a legal clause — is separated from the message by what it means rather than by how it
/// is marked up, and every heuristic tried against that bought one shape of mail and misfired on another.
/// </para>
/// <para>
/// <b>What is asked is the outline and what comes back is a partition.</b> A producer never emits message text, so the
/// document this composes is built from the blocks the reduction already produced: every block it keeps is identical to
/// what the reduced view would have drawn, and fidelity is therefore a property of the contract rather than of how
/// firmly an instruction asked for it.
/// </para>
/// <para>
/// Nothing is derived ahead of a reader and nothing is stored. One open is one proposal, and opening the message again
/// asks again — which is what makes this the same kind of state the reader's ask for remote pictures is, and what keeps a
/// first test of whether the view is worth having from writing a table nobody has decided the shape of.
/// </para>
/// <para>
/// Every failure serves the ordinary reduced document and says which failure it was. The reader is never handed an empty
/// pane and never handed a silently different view, and a producer that dropped every block is read as an answer this
/// cannot use rather than as a message with nothing in it.
/// </para>
/// </remarks>
public sealed class MailBodyCleaning
{
    /// <summary>How many times one body is put to a producer before the ordinary reduced document is served.</summary>
    /// <remarks>
    /// Two, because the shapes this rejects are the ones a producer writes once and not twice: a renumbered answer and a
    /// partition that skips the blocks it found uninteresting. A third attempt buys a reader a longer wait for the same
    /// answer, and the reader is waiting in front of a message the whole time.
    /// </remarks>
    public const int MaximumAttempts = 2;

    private readonly EmailContentReader content;
    private readonly IMailBodyCleaner cleaner;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the pass over the read it cleans and the producer that proposes the cleaning.</summary>
    /// <param name="content">Reads the message from the local copy, under the acting caller's own grant.</param>
    /// <param name="cleaner">Proposes which blocks to keep, or says why it proposed nothing.</param>
    /// <param name="scopeResolver">Answers whose mail this pass is reading, which is the posture the outline is scanned under.</param>
    /// <param name="egressGuard">Names that user for the whole flow, so every text of the outline is guarded under their own posture.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailBodyCleaning(
        EmailContentReader content,
        IMailBodyCleaner cleaner,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(cleaner);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(authorization);

        this.content = content;
        this.cleaner = cleaner;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.authorization = authorization;
    }

    /// <summary>Cleans one of the acting caller's messages, or says why the reduced document is what came back.</summary>
    /// <param name="storedEmailId">The message to draw.</param>
    /// <param name="retainRemoteImageReferences">Whether the reader has asked this message's remote pictures for, so the cleaned document matches what they are looking at.</param>
    /// <param name="cancellationToken">Cancels the pass when the reader stops waiting.</param>
    /// <returns>The rendering, or <see langword="null" /> where this caller holds no such message.</returns>
    public async Task<CleanedMailBody?> CleanAsync(
        StoredEmailId storedEmailId,
        bool retainRemoteImageReferences,
        CancellationToken cancellationToken)
    {
        // The grant is asked for here as well as at the transport boundary, because what this spends is the deployment's
        // provider allowance rather than a local read: an entrypoint added later that holds only the reading grant would
        // otherwise reach a chat call through a use case that never checked.
        this.authorization.RequirePermission(MailFathomPermission.MailAsk);

        var request = GetEmailContentRequest.Create([storedEmailId]) with
        {
            IncludeMailDocument = true,
            RetainRemoteImageReferences = retainRemoteImageReferences,
        };

        var read = await this.content.ReadContentAsync(request, cancellationToken);

        if (read.Emails[0].Content is not { } message)
        {
            return null;
        }

        var document = message.Body.Document;

        if (document is null || document.Refusal != MailDocumentRefusal.None || document.Blocks.Count == 0)
        {
            return new CleanedMailBody(MailBodyCleaningOutcome.NothingToClean, document);
        }

        if (!this.cleaner.IsActive)
        {
            return new CleanedMailBody(MailBodyCleaningOutcome.NotActivated, document);
        }

        var outline = MailBodyCleaningOutline.Describe(document, message.Headers.Subject, SenderOf(message.Headers));

        // The read above opened a scope of its own and closed it again, so the outline would otherwise be guarded on a
        // flow acting for nobody — which a deployment scanning anybody refuses outright rather than degrading. The user
        // is named here instead, where the payload that leaves the deployment is composed.
        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.User);

        return await this.ProposedAsync(outline, document, cancellationToken);
    }

    /// <summary>Names the envelope sender, which is what the rule about a pasted header block is decided against.</summary>
    /// <remarks>
    /// The display name where the message wrote one and the address otherwise, and the <c>From</c> header before the
    /// envelope sender where both are present — that is the order a reader sees them in, and a pasted block repeats
    /// whichever of them the forwarding system had.
    /// </remarks>
    private static string? SenderOf(EmailContentHeaders headers)
    {
        var author = headers.Participants.FirstOrDefault(participant => participant.Role is EmailAddressRole.From)
            ?? headers.Participants.FirstOrDefault(participant => participant.Role is EmailAddressRole.Sender);

        return author is null
            ? null
            : author.Address.DisplayName is { Length: > 0 } displayName ? displayName : author.Address.Address;
    }

    /// <summary>Applies the first proposal that describes the document, or says why none did.</summary>
    private async Task<CleanedMailBody> ProposedAsync(
        CleanableMailBody outline,
        MailDocument document,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            var proposal = await this.cleaner.ProposeAsync(outline, cancellationToken);

            if (proposal.Withholding is not MailBodyCleaningWithholding.None)
            {
                // A withholding is not a rejected answer and is never asked again: a spent period and an endpoint that
                // refused the request both say the same thing twice, and the reader is waiting.
                return new CleanedMailBody(Reporting(proposal.Withholding), document);
            }

            // Nothing kept is an answer this cannot use rather than a message with nothing in it, so it is asked again
            // exactly as a partition that did not describe the document is. A reader is never shown an empty pane.
            if (MailBodyCleaningSegments.Read(proposal.Segments, document.Blocks.Count) is { Count: > 0 } kept)
            {
                return new CleanedMailBody(
                    MailBodyCleaningOutcome.Cleaned,
                    document with { Blocks = [.. kept.Select(index => document.Blocks[index])] });
            }
        }

        return new CleanedMailBody(MailBodyCleaningOutcome.AnswerRejected, document);
    }

    private static MailBodyCleaningOutcome Reporting(MailBodyCleaningWithholding withholding) => withholding switch
    {
        MailBodyCleaningWithholding.AllowanceExhausted => MailBodyCleaningOutcome.AllowanceExhausted,
        MailBodyCleaningWithholding.NotActivated => MailBodyCleaningOutcome.NotActivated,
        _ => MailBodyCleaningOutcome.ProviderUnavailable,
    };
}
