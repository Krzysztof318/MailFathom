// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Accounts;
using MailFathom.Application.Discovery.Citations;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Citations;

/// <summary>Covers following a plan's citations: what resolves, what is reported instead, and what is never read.</summary>
public sealed class CitationResolverTests
{
    /// <summary>The literal the switched-on deployment in these tests detects, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    [Fact]
    public async Task ResolveAsync_CitationOfAMessage_ResolvesToItWithWhereAndWhenItWasRead()
    {
        // Arrange
        var received = new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
        var summary = SyntheticEmailSummaries.Create(receivedAt: received, subject: "Renewal terms");
        var resolver = ResolverOver([summary], HeadersOf("Renewal terms", received));

        // Act
        var resolved = await resolver.ResolveAsync(
            [new EmailCitationTarget(summary.StoredEmailId)],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.Resolved, citation.Outcome);
        Assert.Equal(
            (summary.StoredEmailId, SyntheticEmailSummaries.DefaultAccountId, SyntheticEmailSummaries.DefaultFolderAlias, "Renewal terms", received),
            (citation.Message!.StoredEmailId,
                citation.Message.AccountId.Value,
                citation.Message.FolderAlias.Value,
                citation.Message.Subject,
                citation.Message.ReceivedAt));
        Assert.Null(citation.Fragment);
        Assert.Null(citation.Attachment);
    }

    [Fact]
    public async Task ResolveAsync_CitationOfAFragment_ResolvesToThePassageAndTheOffsetsItWasCutFrom()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var fragment = EmailChunkId.Create(Guid.CreateVersion7());
        var resolver = ResolverOver(
            [summary],
            fragments: FragmentReaderHolding(summary.StoredEmailId, PassageOf(fragment, ordinal: 2, startOffset: 400, "the agreed rate is 4.5%")));

        // Act
        var resolved = await resolver.ResolveAsync(
            [new FragmentCitationTarget(summary.StoredEmailId, fragment)],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.Resolved, citation.Outcome);
        Assert.Equal(
            (fragment, 2, 400, 423, "the agreed rate is 4.5%"),
            (citation.Fragment!.Fragment,
                citation.Fragment.Ordinal,
                citation.Fragment.StartOffset,
                citation.Fragment.EndOffset,
                citation.Fragment.Text));
        Assert.Equal(summary.StoredEmailId, citation.Message!.StoredEmailId);
    }

    [Fact]
    public async Task ResolveAsync_FragmentAReCutReplaced_ReportsTheMessageWithThePlaceUnresolvable()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var resolver = ResolverOver([summary], fragments: FragmentReaderHolding(summary.StoredEmailId));

        // Act
        var resolved = await resolver.ResolveAsync(
            [new FragmentCitationTarget(summary.StoredEmailId, EmailChunkId.Create(Guid.CreateVersion7()))],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.Unresolvable, citation.Outcome);
        Assert.Equal(summary.StoredEmailId, citation.Message!.StoredEmailId);
        Assert.Null(citation.Fragment);
    }

    /// <summary>
    /// One request may name two messages the caller reads, and a passage belongs to exactly one of them: a citation
    /// naming another message's passage reports the place unresolvable rather than publishing that message's text as
    /// the evidence behind a fact attributed elsewhere.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_APassageOfAnotherCitedMessage_ReportsThePlaceUnresolvableOnTheMessageThatNamedIt()
    {
        // Arrange
        var quoting = SyntheticEmailSummaries.Create();
        var quoted = SyntheticEmailSummaries.Create();
        var fragment = EmailChunkId.Create(Guid.CreateVersion7());
        var fragments = Substitute.For<ICitedFragmentReader>();
        fragments
            .ReadFragmentsAsync(Arg.Any<StoredEmailId>(), Arg.Any<IReadOnlyCollection<EmailChunkId>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<EmailChunkId, CitedFragment>>(
                call.ArgAt<StoredEmailId>(0) == quoted.StoredEmailId
                    ? new Dictionary<EmailChunkId, CitedFragment>
                    {
                        [fragment] = PassageOf(fragment, ordinal: 0, startOffset: 0, "the agreed rate is 4.5%"),
                    }
                    : new Dictionary<EmailChunkId, CitedFragment>()));

        var resolver = ResolverOver([quoting, quoted], fragments: fragments);

        // Act
        var resolved = await resolver.ResolveAsync(
            [
                new FragmentCitationTarget(quoting.StoredEmailId, fragment),
                new FragmentCitationTarget(quoted.StoredEmailId, fragment),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (quoting.StoredEmailId, CitationResolutionOutcome.Unresolvable, (string?)null),
                (quoted.StoredEmailId, CitationResolutionOutcome.Resolved, "the agreed rate is 4.5%"),
            ],
            resolved.Select(citation => (citation.StoredEmailId, citation.Outcome, citation.Fragment?.Text)));
    }

    [Fact]
    public async Task ResolveAsync_CitationOfAnAttachment_ResolvesToTheFileAtThatPosition()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 2);
        var resolver = ResolverOver(
            [summary],
            attachments: [AttachmentOf("cover.txt", "text/plain", 12), AttachmentOf("terms.pdf", "application/pdf", 8192)]);

        // Act
        var resolved = await resolver.ResolveAsync(
            [new AttachmentCitationTarget(summary.StoredEmailId, attachmentPosition: 1)],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.Resolved, citation.Outcome);
        Assert.Equal(
            (1, "terms.pdf", "application/pdf", 8192L),
            (citation.Attachment!.Position,
                citation.Attachment.FileName,
                citation.Attachment.MediaType,
                citation.Attachment.SizeOctets));
    }

    [Fact]
    public async Task ResolveAsync_AttachmentPositionTheMessageNoLongerCarries_ReportsUnresolvableRatherThanAnotherFile()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var resolver = ResolverOver([summary], attachments: [AttachmentOf("cover.txt", "text/plain", 12)]);

        // Act
        var resolved = await resolver.ResolveAsync(
            [new AttachmentCitationTarget(summary.StoredEmailId, attachmentPosition: 3)],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.Unresolvable, citation.Outcome);
        Assert.Null(citation.Attachment);
    }

    [Fact]
    public async Task ResolveAsync_MessageThisCallerMayNotRead_ReportsAPrivateSourceCarryingNothingElse()
    {
        // Arrange
        var somebodyElses = StoredEmailId.Create(Guid.CreateVersion7());
        var resolver = ResolverOver([]);

        // Act
        var resolved = await resolver.ResolveAsync(
            [new EmailCitationTarget(somebodyElses)],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.PrivateSource, citation.Outcome);
        Assert.Equal(somebodyElses, citation.StoredEmailId);
        Assert.Null(citation.Message);
    }

    [Fact]
    public async Task ResolveAsync_FragmentOfAMessageThisCallerMayNotRead_ReadsNoPassageAtAll()
    {
        // Arrange
        var fragments = Substitute.For<ICitedFragmentReader>();
        var resolver = ResolverOver([], fragments: fragments);

        // Act
        var resolved = await resolver.ResolveAsync(
            [new FragmentCitationTarget(StoredEmailId.Create(Guid.CreateVersion7()), EmailChunkId.Create(Guid.CreateVersion7()))],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CitationResolutionOutcome.PrivateSource, Assert.Single(resolved).Outcome);
        await fragments
            .DidNotReceiveWithAnyArgs()
            .ReadFragmentsAsync(default, [], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveAsync_DamagedLocalCopy_ReportsItUnresolvableRatherThanPrivate()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var resolver = ResolverOver([summary], contentStore: ContentStoreReturning(storedContent: null));

        // Act
        var resolved = await resolver.ResolveAsync(
            [new EmailCitationTarget(summary.StoredEmailId)],
            TestContext.Current.CancellationToken);

        // Assert
        var citation = Assert.Single(resolved);
        Assert.Equal(CitationResolutionOutcome.Unresolvable, citation.Outcome);
        Assert.Null(citation.Message);
    }

    [Fact]
    public async Task ResolveAsync_SeveralCitations_AnswersOneEachInTheOrderTheRequestNamedThem()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var fragment = EmailChunkId.Create(Guid.CreateVersion7());
        var somebodyElses = StoredEmailId.Create(Guid.CreateVersion7());
        var resolver = ResolverOver(
            [summary],
            fragments: FragmentReaderHolding(summary.StoredEmailId, PassageOf(fragment, ordinal: 0, startOffset: 0, "as agreed")));

        // Act
        var resolved = await resolver.ResolveAsync(
            [
                new EmailCitationTarget(somebodyElses),
                new FragmentCitationTarget(summary.StoredEmailId, fragment),
                new EmailCitationTarget(summary.StoredEmailId),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (somebodyElses, CitationResolutionOutcome.PrivateSource),
                (summary.StoredEmailId, CitationResolutionOutcome.Resolved),
                (summary.StoredEmailId, CitationResolutionOutcome.Resolved),
            ],
            resolved.Select(citation => (citation.StoredEmailId, citation.Outcome)));
    }

    [Fact]
    public async Task ResolveAsync_SeveralCitationsOfOneMessage_ReadsThatMessageOnce()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var renderer = RendererReturning(RenderingOf(
            HeadersOf("Subject", receivedAt: null),
            [AttachmentOf("cover.txt", "text/plain", 12)]));
        var resolver = ResolverOver([summary], renderer: renderer);

        // Act
        await resolver.ResolveAsync(
            [
                new EmailCitationTarget(summary.StoredEmailId),
                new AttachmentCitationTarget(summary.StoredEmailId, attachmentPosition: 0),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        await renderer.Received(1).RenderAsync(
            Arg.Any<StoredEmailContent>(),
            Arg.Any<EmailContentRenderingBounds>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_ADetectedValueInThePassage_PublishesItRedacted()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var summary = SyntheticEmailSummaries.Create();
        var fragment = EmailChunkId.Create(Guid.CreateVersion7());
        var resolver = ResolverOver(
            [summary],
            fragments: FragmentReaderHolding(summary.StoredEmailId, PassageOf(fragment, ordinal: 0, startOffset: 0, $"the key is {Marker}")),
            egressGuard: egress.Guard);

        // Act
        var resolved = await resolver.ResolveAsync(
            [new FragmentCitationTarget(summary.StoredEmailId, fragment)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, Assert.Single(resolved).Fragment!.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_MoreCitationsThanOneRequestFollows_RefusesTheRequest()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var resolver = ResolverOver([summary]);
        var citations = Enumerable
            .Range(0, CitationResolver.MaximumCitations + 1)
            .Select(_ => new EmailCitationTarget(summary.StoredEmailId))
            .ToArray();

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            resolver.ResolveAsync(citations, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("citations", refusal.ParamName);
    }

    [Fact]
    public async Task ResolveAsync_NoCitationAtAll_RefusesTheRequest()
    {
        // Arrange
        var resolver = ResolverOver([]);

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            resolver.ResolveAsync([], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("citations", refusal.ParamName);
    }

    /// <summary>Builds the use case over a deployment holding exactly the messages named.</summary>
    /// <remarks>
    /// The messages are the whole of what this caller may read: a summary reader that answers for nothing else is what
    /// a citation naming somebody else's mail meets, which is the same thing the content read's own scope produces.
    /// </remarks>
    private static CitationResolver ResolverOver(
        IReadOnlyList<EmailSummary> summaries,
        EmailContentHeaders? headers = null,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null,
        ICitedFragmentReader? fragments = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IEmailContentStore? contentStore = null,
        IEmailContentRenderer? renderer = null)
    {
        var accountCatalog = CatalogServing(MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId));

        return new CitationResolver(
            new EmailContentReader(
                SummaryReaderOver(summaries),
                new StubEmailThreadReader(),
                contentStore ?? ContentStoreReturning(IntactContent()),
                renderer ?? RendererReturning(RenderingOf(headers ?? HeadersOf("Subject", receivedAt: null), attachments)),
                new RecordingEmailContentRepairRequestStore(),
                ScopeResolverOver(accountCatalog, summaries),
                new RecordingAttachmentDownloadLinkIssuer(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmailContentReadOptions(),
                new RecordingMailboxReadTelemetry(),
                AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead)),
            fragments ?? Substitute.For<ICitedFragmentReader>(),
            ScopeResolverOver(accountCatalog, summaries),
            egressGuard ?? SensitiveContentEgressGuards.Inactive());
    }

    private static MailboxScopeResolver ScopeResolverOver(
        ICallerMailAccountCatalog accountCatalog,
        IReadOnlyList<EmailSummary> summaries) => new(
        accountCatalog,
        StubMailFolderParticipation.Mapping(
            [.. summaries.Select(summary => new MailFolderIdentity(summary.AccountId, summary.FolderAlias))]),
        StubJunkMailFolderCatalog.None,
        StubMailFolderMappings.ResolvingNothing);

    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([.. servedAccountIds.Select(accountId => SyntheticServedAccount.Of(accountId))]);
        catalog.Owner.Returns(SyntheticMailOwner.Deployment);

        return catalog;
    }

    private static IStoredEmailSummaryReader SummaryReaderOver(IReadOnlyList<EmailSummary> summaries)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                summaries.FirstOrDefault(summary => summary.StoredEmailId == call.Arg<StoredEmailId>())));

        return reader;
    }

    private static ICitedFragmentReader FragmentReaderHolding(
        StoredEmailId storedEmailId,
        params CitedFragment[] passages)
    {
        var reader = Substitute.For<ICitedFragmentReader>();
        reader
            .ReadFragmentsAsync(storedEmailId, Arg.Any<IReadOnlyCollection<EmailChunkId>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<EmailChunkId, CitedFragment>>(
                passages
                    .Where(passage => call.ArgAt<IReadOnlyCollection<EmailChunkId>>(1).Contains(passage.Fragment))
                    .ToDictionary(static passage => passage.Fragment)));

        return reader;
    }

    private static CitedFragment PassageOf(EmailChunkId fragment, int ordinal, int startOffset, string text) =>
        new(fragment, ordinal, startOffset, startOffset + text.Length, text);

    private static IEmailContentStore ContentStoreReturning(StoredEmailContent? storedContent)
    {
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    private static IEmailContentRenderer RendererReturning(EmailContentRendering rendering)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(rendering)));

        return renderer;
    }

    private static EmailContentRendering RenderingOf(
        EmailContentHeaders headers,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null) => new(
        headers,
        new EmailBodyRepresentation("Body", 4, EmailBodyTruncation.None),
        SanitizedHtmlBody: null,
        new EmailBodyForms(PlainText: true, Html: false),
        BodyIsEncrypted: false,
        EmailAttachmentSummary.Create(
            attachments ?? [],
            inlineResourceCount: 0,
            isEncrypted: false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false),
        attachments ?? []);

    private static EmailContentHeaders HeadersOf(string? subject, DateTimeOffset? receivedAt) => new(
        subject,
        receivedAt,
        receivedAt,
        [],
        EmailThreadReferences.None);

    private static ExtractedEmailAttachment AttachmentOf(string fileName, string mediaType, long sizeOctets) =>
        new(
            AttachmentFileName.TryNormalize(fileName, out var normalized)
                ? normalized
                : throw new InvalidOperationException($"'{fileName}' is not a usable attachment file name."),
            mediaType,
            sizeOctets);

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));
}
