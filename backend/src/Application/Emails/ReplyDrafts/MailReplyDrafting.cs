// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>Drafts a reply to one of the acting user's messages, out of the conversation and the way they write.</summary>
/// <remarks>
/// <para>
/// <b>It produces a local artifact and takes no act.</b> Nothing here sends, queues, or writes to a mail server, and
/// nothing is stored: the reply comes back as text the person owns, and saving it as a draft or sending it stays where
/// those acts already are, behind somebody confirming them.
/// </para>
/// <para>
/// The correspondence is read under the same scope the conversation screen reads it under — every account this user
/// holds, no folder narrowing, junk included — because a reply is answered across the exchange rather than inside a
/// folder. A message the scope does not admit is a message this use case has none of, so the drafting answers exactly
/// as it does for a message nobody holds.
/// </para>
/// <para>
/// The style is the account's own sent mail and nothing else, and the read that fetches it is the read that resolved
/// the answered message, which is what keeps one person's manner out of another account's reply. An operator who
/// turned that derivation off leaves the bound at zero: no sent mail is read, and the draft is written from the
/// conversation alone.
/// </para>
/// <para>
/// Both halves of the privacy posture are here rather than in the writer. What goes out to a provider is guarded by
/// the writer at the prompt; what comes back is guarded here before it reaches a client, because a draft is written out
/// of somebody's correspondence and carries whatever that correspondence carried.
/// </para>
/// </remarks>
public sealed class MailReplyDrafting
{
    /// <summary>How many of the conversation's most recent messages one drafting is grounded in.</summary>
    /// <remarks>
    /// A reply answers where a conversation got to, so the recent end of it is what a draft is written from; the
    /// opening of a long exchange is what its derived state is for. The bound is a constant rather than a setting
    /// because it decides how long one drafting takes rather than what a deployment is — what an operator declares
    /// about the spend is the answering period's ceilings, which every drafting is admitted against.
    /// </remarks>
    public const int MaximumMessages = 20;

    /// <summary>How much of one of those messages travels with the drafting.</summary>
    /// <remarks>The bound a conversation's own derivation reads a message under, because what is being bounded is the same text: what a message added, with the history it quoted already trimmed off.</remarks>
    public const int MaximumCharactersPerMessage = 4_000;

    /// <summary>How many of the person's own recent sent messages a style is derived from.</summary>
    /// <remarks>
    /// Enough for a manner to be visible — how they open, how they sign off, whether they write in paragraphs or in
    /// lines — and few enough that drafting one reply never turns into reading somebody's outbox. Every one of them is
    /// mail this deployment already holds and never leaves it except as this drafting's own prompt.
    /// </remarks>
    public const int MaximumStyleMessages = 6;

    /// <summary>How much of one sent message travels for that derivation.</summary>
    /// <remarks>Shorter than a message of the conversation, because what is being read out of it is a manner rather than a meaning, and the opening of a reply is where a manner shows.</remarks>
    public const int MaximumStyleCharactersPerMessage = 1_200;

    private readonly IReplyDraftSourceReader sourceReader;
    private readonly IReplyDraftWriter writer;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly AccessAuthorization authorization;
    private readonly bool derivesStyleFromSentMail;

    /// <summary>Initializes the drafting.</summary>
    /// <param name="sourceReader">Reads the conversation being answered and the manner its account writes in.</param>
    /// <param name="writer">Writes the reply, which is the one part of this that reaches a provider.</param>
    /// <param name="scopeResolver">Decides which accounts and folders the conversation is read across.</param>
    /// <param name="egressGuard">Scans what the draft publishes to a client, where this deployment scans anything.</param>
    /// <param name="authorization">Enforces the permission this drafting is behind.</param>
    /// <param name="derivesStyleFromSentMail">Whether the deployment derives a manner from the account's own sent mail at all.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is <see langword="null" />.</exception>
    public MailReplyDrafting(
        IReplyDraftSourceReader sourceReader,
        IReplyDraftWriter writer,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        AccessAuthorization authorization,
        bool derivesStyleFromSentMail)
    {
        ArgumentNullException.ThrowIfNull(sourceReader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(authorization);

        this.sourceReader = sourceReader;
        this.writer = writer;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.authorization = authorization;
        this.derivesStyleFromSentMail = derivesStyleFromSentMail;
    }

    /// <summary>Drafts a reply to one of the acting user's messages.</summary>
    /// <param name="request">Which message is being answered, and what its author asked the reply to say.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The draft, <see cref="ReplyDraft.Nothing" /> where none could be written, or <see langword="null" /> where this
    /// user holds no such message.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when this deployment has spent what it allows a provider for the period.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the draft carries, which withholds it rather than publishing it unscanned.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailAsk" />.</exception>
    public async Task<ReplyDraft?> DraftAsync(ReplyDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The grant that puts mail in front of a chat provider rather than the one that reads it, because that is what
        // this does: the conversation and a sample of the account's own sent mail leave this deployment, and the call
        // is charged to the same allowance a question is.
        this.authorization.RequirePermission(MailFathomPermission.MailAsk);

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.User);

        var scope = this.scopeResolver.ReadableScope([], [], JunkMailInclusion.Included);

        if (scope.AccountIds.Count is 0)
        {
            return null;
        }

        var sources = await this.sourceReader.ReadSourcesAsync(
            request.AnsweredEmailId,
            scope,
            this.Bounds(),
            cancellationToken);

        if (sources is null)
        {
            return null;
        }

        if (sources.Messages.Count is 0)
        {
            // A conversation this deployment has stored no readable text for. There is nothing to ground a reply in,
            // and asking a provider to write one anyway would produce a fluent message resting on nothing at all.
            return ReplyDraft.Nothing;
        }

        var brief = new ReplyDraftBrief(
            sources,
            Bounded(request.Selection, ReplyDraftRequest.MaximumSelectionLength),
            Bounded(request.Instruction, ReplyDraftRequest.MaximumInstructionLength));

        var draft = await this.writer.WriteAsync(brief, cancellationToken);

        return draft.WasWritten ? await this.GuardedAsync(draft, cancellationToken) : draft;
    }

    /// <summary>Cuts what somebody typed down to what one drafting may carry of it.</summary>
    /// <remarks>
    /// Shortened rather than refused, because the boundary above already refuses in the words of the bound and this is
    /// what makes the rule hold for every caller rather than for the one that was written against it.
    /// </remarks>
    private static string? Bounded(string? typed, int maximumLength) =>
        string.IsNullOrWhiteSpace(typed)
            ? null
            : MailTextBounds.TruncateAtTextElementBoundary(typed.Trim(), maximumLength);

    private ReplyDraftBounds Bounds() => new(
        MaximumMessages,
        MaximumCharactersPerMessage,
        this.derivesStyleFromSentMail ? MaximumStyleMessages : 0,
        MaximumStyleCharactersPerMessage);

    /// <summary>Scans everything the draft would publish, under the point this surface is read on.</summary>
    /// <remarks>
    /// One report for the draft rather than one per claim, because the draft is what a composer waits for. The body and
    /// the claims are offered; the addresses are not, being values this deployment resolved out of its own store rather
    /// than text a producer wrote, and a redacted address would be one nobody could send to.
    /// </remarks>
    private async Task<ReplyDraft> GuardedAsync(ReplyDraft draft, CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return draft;
        }

        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientReplyDraft,
            cancellationToken);

        var body = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.ClientReplyDraft,
            draft.Body,
            cancellationToken);

        var claims = new List<ReplyDraftClaim>(draft.Claims.Count);

        foreach (var claim in draft.Claims)
        {
            claims.Add(ReplyDraftClaim.Create(
                await this.egressGuard.GuardAsync(
                    SensitiveContentEgressPoint.ClientReplyDraft,
                    claim.Text,
                    cancellationToken),
                claim.Sources));
        }

        scan.Completed();

        return ReplyDraft.Written(body, claims, draft.ProposedRecipients);
    }
}
