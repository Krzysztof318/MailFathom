// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Discovery.Citations;

/// <summary>Follows the citations a presentation plan declared to the mail they were drawn from.</summary>
/// <remarks>
/// <para>
/// One route for every citation a plan can hold, which is what makes the contract behind a rendered answer checkable:
/// a block naming a source and a block naming a passage of the same message are followed the same way, so a client
/// draws one evidence affordance rather than one per block type.
/// </para>
/// <para>
/// <b>Resolution is an access decision before it is a lookup.</b> Every message is read through the same use case the
/// reading pane reads one with, so the accounts and folders the caller's owner may read decide what a citation resolves
/// to, and a citation composed to name somebody else's mail reads nothing. A source outside that scope is reported as
/// private rather than as a failure: a plan is a thing to be shared, and a reader shown a fact whose source they may
/// not open has to be able to tell that from an answer that is broken.
/// </para>
/// <para>
/// <b>A citation outlives a re-cut of the mail it points at.</b> Chunking derives a message's passages again whenever
/// its stored reading changes, so a passage identifier is the part of a citation that can go while the message it was
/// cut from stays. The target carries both, which is why the resolution of a passage that no longer exists is the
/// message with the place reported unresolvable rather than a citation that leads nowhere — and never the nearest
/// remaining passage, which would be evidence for a fact it was not drawn from.
/// </para>
/// <para>
/// <b>What one request may draw out is bounded twice, and neither bound is the caller's.</b> The count is
/// <see cref="MaximumCitations" />, which is what makes the number of messages read bounded as well; the size is the
/// chunking rules', a passage being cut to a fixed length long before it is cited. Nothing about a request widens
/// either.
/// </para>
/// <para>
/// Everything a resolution carries beyond an identity and an outcome is mail content. The passage crosses the guarded
/// egress this use case opens, the message's own values crossed the one the content read opened, and none of it reaches
/// a log, a span, or a telemetry event.
/// </para>
/// </remarks>
public sealed class CitationResolver
{
    /// <summary>The greatest number of citations one request may name.</summary>
    /// <remarks>
    /// It is the count half of the bound on how much mail one resolution draws out, and it is set to the bound the
    /// content read already applies to the messages it names: citations may repeat a message, so this many citations
    /// can never name more messages than <see cref="GetEmailContentRequest.MaximumEmails" /> admits. A block carries a
    /// handful of citations and a client resolves the ones a reader is looking at, so a request past this is a screen
    /// asking for a plan's whole source list at once rather than a reader checking a fact.
    /// </remarks>
    public const int MaximumCitations = GetEmailContentRequest.MaximumEmails;

    private readonly EmailContentReader content;
    private readonly ICitedFragmentReader fragments;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;

    /// <summary>Initializes the use case.</summary>
    /// <param name="content">Reads a cited message from the local copy, under the caller's own scope.</param>
    /// <param name="fragments">Reads the persisted passages a citation names within a message.</param>
    /// <param name="scopeResolver">Answers whose mail this work is acting for.</param>
    /// <param name="egressGuard">Scans a passage before it crosses to the caller, where this deployment scans anything.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public CitationResolver(
        EmailContentReader content,
        ICitedFragmentReader fragments,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.content = content;
        this.fragments = fragments;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
    }

    /// <summary>Follows every citation one request names.</summary>
    /// <param name="citations">What to follow, in the order the caller named them.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One resolution per citation, in the order the request named them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="citations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the request names no citation, or more than <see cref="MaximumCitations" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work in hand is acting for no owner.</exception>
    /// <remarks>
    /// The order is the contract, as it is for the content read this is built on: a resolution names the message it
    /// answers for and nothing else the caller sent, so position is how a caller pairs an answer with the citation it
    /// asked about. The same message cited twice is read once.
    /// </remarks>
    public async Task<IReadOnlyList<ResolvedCitation>> ResolveAsync(
        IReadOnlyList<PresentationCitationTarget> citations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentOutOfRangeException.ThrowIfZero(citations.Count, nameof(citations));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(citations.Count, MaximumCitations, nameof(citations));

        var messages = await this.ReadMessagesAsync(citations, cancellationToken);
        var passages = await this.ReadPassagesAsync(citations, messages, cancellationToken);

        return [.. citations.Select(citation => Resolve(citation, messages, passages))];
    }

    /// <summary>Reads every message the citations name, once each, under the caller's own scope.</summary>
    /// <returns>The readings, keyed by identity, holding no entry for a message the caller may not read.</returns>
    /// <remarks>
    /// A message that is stored and whose copy is damaged is kept apart from one this caller may not read, because the
    /// two are different answers: the read has already recorded a repair request for the first, and only the first is
    /// worth asking about again.
    /// </remarks>
    private async Task<IReadOnlyDictionary<StoredEmailId, EmailContentReadOutcome>> ReadMessagesAsync(
        IReadOnlyList<PresentationCitationTarget> citations,
        CancellationToken cancellationToken)
    {
        var named = citations.Select(static citation => citation.Email).Distinct().ToArray();

        // No representation is asked for and no link is minted: what a resolution publishes is where a fact came from,
        // and a capability nobody asked for would be a bearer credential this response handed out. The parse still
        // happens, because a cited file is described from the stored message rather than from a row.
        var read = await this.content.ReadContentAsync(GetEmailContentRequest.Create(named), cancellationToken);

        return read.Emails.ToDictionary(static outcome => outcome.StoredEmailId);
    }

    /// <summary>Reads the passages the citations name within the messages that could be read, and guards them.</summary>
    /// <returns>The passages found, keyed by their identifiers, holding no entry for one the store no longer has.</returns>
    /// <remarks>
    /// A passage is read per message rather than by identifier alone, so a citation naming a passage of a message the
    /// caller may not read has nothing to read it from. The guard is opened once around the whole publication rather
    /// than per passage, because what a scan costs is paid per request.
    /// </remarks>
    private async Task<IReadOnlyDictionary<EmailChunkId, CitedFragment>> ReadPassagesAsync(
        IReadOnlyList<PresentationCitationTarget> citations,
        IReadOnlyDictionary<StoredEmailId, EmailContentReadOutcome> messages,
        CancellationToken cancellationToken)
    {
        var cited = citations
            .OfType<FragmentCitationTarget>()
            .Where(citation => messages.TryGetValue(citation.Email, out var outcome) && outcome.Content is not null)
            .GroupBy(static citation => citation.Email)
            .ToArray();

        if (cited.Length is 0)
        {
            return new Dictionary<EmailChunkId, CitedFragment>();
        }

        var found = new Dictionary<EmailChunkId, CitedFragment>();

        foreach (var message in cited)
        {
            var passages = await this.fragments.ReadFragmentsAsync(
                message.Key,
                [.. message.Select(static citation => citation.Fragment).Distinct()],
                cancellationToken);

            foreach (var passage in passages)
            {
                found[passage.Key] = passage.Value;
            }
        }

        return await this.GuardedAsync(found, cancellationToken);
    }

    /// <summary>Scans every passage about to cross to the caller, where this deployment scans anything.</summary>
    private async Task<IReadOnlyDictionary<EmailChunkId, CitedFragment>> GuardedAsync(
        Dictionary<EmailChunkId, CitedFragment> passages,
        CancellationToken cancellationToken)
    {
        if (!this.egressGuard.IsActive)
        {
            return passages;
        }

        using var actingFor = this.egressGuard.ActingFor(this.scopeResolver.Owner);
        using var scan = this.egressGuard.BeginGuardedOperation(
            SensitiveContentEgressPoint.ClientCitationResolution,
            cancellationToken);

        var cited = passages.Keys.ToArray();
        var guarded = await this.egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ClientCitationResolution,
            [.. cited.Select(fragment => passages[fragment].Text)],
            cancellationToken);

        scan.Completed();

        return cited
            .Select((fragment, position) => (Fragment: fragment, Passage: passages[fragment] with { Text = guarded[position] }))
            .ToDictionary(static found => found.Fragment, static found => found.Passage);
    }

    /// <summary>Answers for one citation from what the reads produced.</summary>
    private static ResolvedCitation Resolve(
        PresentationCitationTarget citation,
        IReadOnlyDictionary<StoredEmailId, EmailContentReadOutcome> messages,
        IReadOnlyDictionary<EmailChunkId, CitedFragment> passages)
    {
        if (!messages.TryGetValue(citation.Email, out var outcome))
        {
            return ResolvedCitation.PrivateSource(citation.Email);
        }

        if (outcome.Content is not { } message)
        {
            return outcome.Failure?.ErrorCode == MailFathomErrorCode.EmailContentUnavailable
                ? ResolvedCitation.Unresolvable(citation.Email)
                : ResolvedCitation.PrivateSource(citation.Email);
        }

        var source = CitedMessageOf(message);

        // The remaining case is the citation that names the message as such, the target hierarchy being closed to these
        // three.
        return citation switch
        {
            FragmentCitationTarget fragment => passages.TryGetValue(fragment.Fragment, out var passage)
                ? ResolvedCitation.Resolved(source, passage)
                : ResolvedCitation.Unresolvable(source),
            AttachmentCitationTarget attachment => AttachmentOf(message, attachment.AttachmentPosition) is { } file
                ? ResolvedCitation.Resolved(source, file)
                : ResolvedCitation.Unresolvable(source),
            _ => ResolvedCitation.Resolved(source),
        };
    }

    /// <summary>Describes the message a resolution belongs to, from the reading the content use case produced.</summary>
    private static CitedMessage CitedMessageOf(ReadEmailContent message) => new(
        message.StoredEmailId,
        message.AccountId,
        message.FolderAlias,
        message.Headers.Subject,
        message.Headers.SentAt,
        message.Headers.ReceivedAt);

    /// <summary>Describes the file at one position, or reports that the message carries none there.</summary>
    /// <remarks>
    /// The position is resolved against the same order the message read publishes its attachments in, which is the
    /// order the structure is walked and the one the download route is addressed with. A message whose parts have
    /// changed since the citation was written carries fewer of them, which is a place that is gone rather than a
    /// different file.
    /// </remarks>
    private static CitedAttachment? AttachmentOf(ReadEmailContent message, int position) =>
        position < message.Attachments.Count
            ? new CitedAttachment(
                position,
                message.Attachments[position].Description.FileName?.Value,
                message.Attachments[position].Description.MediaType,
                message.Attachments[position].Description.DecodedSizeOctets)
            : null;
}
