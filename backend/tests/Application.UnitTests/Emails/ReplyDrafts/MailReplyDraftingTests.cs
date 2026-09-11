// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ReplyDrafts;

/// <summary>Covers the drafting a composer asks for: what it reads, what it sends, and what it publishes back.</summary>
/// <remarks>
/// What the queries themselves admit is the store's and is asserted where that predicate lives. What is asserted here
/// is the scope the drafting composes, the permission it is behind, the bounds it reads under — the style one above
/// all, which is the operator's switch — and that nothing reaches a client unscanned.
/// </remarks>
public sealed class MailReplyDraftingTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly StoredEmailId Answered = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public async Task DraftAsync_AConversationTheWriterAnswered_PublishesTheDraft()
    {
        // Arrange
        var written = Written("Tuesday at ten works.");
        var drafting = CreateDrafting(SourceReaderReturning(Sources()), WriterReturning(written));

        // Act
        var draft = await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(draft);
        Assert.True(draft.WasWritten);
        Assert.Equal("Tuesday at ten works.", draft.Body);
    }

    /// <summary>A message this user does not hold answers exactly as one nobody holds, and costs no provider call.</summary>
    [Fact]
    public async Task DraftAsync_AMessageTheScopeDoesNotAdmit_AnswersWithNothingAndDraftsNothing()
    {
        // Arrange
        var writer = WriterReturning(Written("Tuesday at ten works."));
        var drafting = CreateDrafting(SourceReaderReturning(null), writer);

        // Act
        var draft = await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(draft);
        await writer.DidNotReceiveWithAnyArgs().WriteAsync(Arg.Any<ReplyDraftBrief>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A deployment serving this caller no account holds no message of theirs, so nothing is read at all.</summary>
    [Fact]
    public async Task DraftAsync_ACallerServedNoAccount_AnswersWithNothingAndReadsNothing()
    {
        // Arrange
        var sourceReader = SourceReaderReturning(Sources());
        var drafting = CreateDrafting(sourceReader, WriterReturning(Written("We accept.")), servedAccounts: []);

        // Act
        var draft = await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(draft);
        await sourceReader.DidNotReceiveWithAnyArgs().ReadSourcesAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<MailboxScope>(),
            Arg.Any<ReplyDraftBounds>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing grounds a reply to a conversation this deployment stored no readable text for.</summary>
    [Fact]
    public async Task DraftAsync_AConversationWithNoReadableMessage_DraftsNothingWithoutReachingTheWriter()
    {
        // Arrange
        var writer = WriterReturning(Written("We accept."));
        var drafting = CreateDrafting(SourceReaderReturning(Sources(messages: [])), writer);

        // Act
        var draft = await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(draft);
        Assert.False(draft.WasWritten);
        await writer.DidNotReceiveWithAnyArgs().WriteAsync(Arg.Any<ReplyDraftBrief>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A reply is answered across the exchange rather than inside a folder, junk included.</summary>
    [Fact]
    public async Task DraftAsync_AnyMessage_ReadsItAcrossTheSameMailTheConversationIsReadAcross()
    {
        // Arrange
        var sourceReader = SourceReaderReturning(Sources());
        var drafting = CreateDrafting(sourceReader, WriterReturning(Written("We accept.")));

        // Act
        await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        await sourceReader.Received(1).ReadSourcesAsync(
            Answered,
            Arg.Is<MailboxScope>(scope =>
                scope!.AccountIds.Contains(Account)
                && scope.SelectedFolders.Count == 0
                && scope.IncludesJunkMail),
            Arg.Any<ReplyDraftBounds>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The operator's switch is the bound: off is no sent mail read rather than a manner derived more quietly.</summary>
    [Theory]
    [InlineData(true, MailReplyDrafting.MaximumStyleMessages)]
    [InlineData(false, 0)]
    public async Task DraftAsync_ADeploymentDecidingAboutStyle_ReadsThatManySentMessages(
        bool derivesStyle,
        int expectedStyleMessages)
    {
        // Arrange
        var sourceReader = SourceReaderReturning(Sources());
        var drafting = CreateDrafting(
            sourceReader,
            WriterReturning(Written("We accept.")),
            derivesStyleFromSentMail: derivesStyle);

        // Act
        await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        await sourceReader.Received(1).ReadSourcesAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<MailboxScope>(),
            Arg.Is<ReplyDraftBounds>(bounds => bounds!.MaximumStyleMessages == expectedStyleMessages),
            Arg.Any<CancellationToken>());
    }

    /// <summary>What somebody typed is bounded before it is put in front of a provider, whatever the boundary let through.</summary>
    [Fact]
    public async Task DraftAsync_AnInstructionAndASelectionPastTheirBounds_SendsTheWriterTheBoundedText()
    {
        // Arrange
        var writer = WriterReturning(Written("We accept."));
        var drafting = CreateDrafting(SourceReaderReturning(Sources()), writer);
        var request = Request() with
        {
            Instruction = new string('i', ReplyDraftRequest.MaximumInstructionLength + 40),
            Selection = new string('s', ReplyDraftRequest.MaximumSelectionLength + 40),
        };

        // Act
        await drafting.DraftAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await writer.Received(1).WriteAsync(
            Arg.Is<ReplyDraftBrief>(brief =>
                brief!.Instruction!.Length == ReplyDraftRequest.MaximumInstructionLength
                && brief.Selection!.Length == ReplyDraftRequest.MaximumSelectionLength),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task DraftAsync_ARequestTypingNothing_SendsTheWriterNoSelectionAndNoInstruction(string? typed)
    {
        // Arrange
        var writer = WriterReturning(Written("We accept."));
        var drafting = CreateDrafting(SourceReaderReturning(Sources()), writer);
        var request = Request() with { Instruction = typed, Selection = typed };

        // Act
        await drafting.DraftAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await writer.Received(1).WriteAsync(
            Arg.Is<ReplyDraftBrief>(brief => brief!.Instruction == null && brief.Selection == null),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The drafting is what puts mail in front of a provider, so it is behind the grant that governs doing so.</summary>
    [Fact]
    public async Task DraftAsync_ACallerWithoutTheMailAskGrant_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var sourceReader = SourceReaderReturning(Sources());
        var drafting = CreateDrafting(
            sourceReader,
            WriterReturning(Written("We accept.")),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act and assert
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            drafting.DraftAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomPermission.MailAsk, refusal.RequiredPermission);
        await sourceReader.DidNotReceiveWithAnyArgs().ReadSourcesAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<MailboxScope>(),
            Arg.Any<ReplyDraftBounds>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A draft is written out of somebody's correspondence and carries whatever it carried, so the body and every claim
    /// are scanned on the way back — while the addresses are this deployment's own values a redaction would only make
    /// unsendable.
    /// </summary>
    [Fact]
    public async Task DraftAsync_ADeploymentThatScans_ScansTheBodyAndTheClaimsAndLeavesTheRecipients()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var written = ReplyDraft.Written(
            $"The key is {Marker}, as agreed.",
            [ReplyDraftClaim.Create($"They sent {Marker}.", [Answered])],
            [Recipient()]);
        var drafting = CreateDrafting(
            SourceReaderReturning(Sources()),
            WriterReturning(written),
            egressGuard: egress.Guard);

        // Act
        var draft = await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(draft);
        Assert.DoesNotContain(Marker, draft.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, Assert.Single(draft.Claims).Text, StringComparison.Ordinal);
        Assert.Equal([Answered], Assert.Single(draft.Claims).Sources);
        Assert.Equal("karolina@example.test", Assert.Single(draft.ProposedRecipients).Address);
    }

    /// <summary>A composer with nothing behind it drafts from what its author typed, reading no correspondence at all.</summary>
    [Fact]
    public async Task DraftAsync_NoAnsweredMessage_DraftsFromTheInstructionWithoutReadingAnyCorrespondence()
    {
        // Arrange
        var sourceReader = SourceReaderReturning(Sources());
        var writer = WriterReturning(Written("We are raising the cap to 5%."));
        var drafting = CreateDrafting(sourceReader, writer);

        // Act
        var draft = await drafting.DraftAsync(
            new ReplyDraftRequest { Instruction = "Ask Contoso for a 5% CPI cap." },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(draft);
        Assert.True(draft.WasWritten);
        await sourceReader.DidNotReceiveWithAnyArgs().ReadSourcesAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<MailboxScope>(),
            Arg.Any<ReplyDraftBounds>(),
            Arg.Any<CancellationToken>());

        var brief = (ReplyDraftBrief)writer.ReceivedCalls().Single().GetArguments()[0]!;

        Assert.Empty(brief.Sources.Messages);
        Assert.Empty(brief.Sources.Participants);
        Assert.Equal("Ask Contoso for a 5% CPI cap.", brief.Instruction);
    }

    /// <summary>Nothing behind it and nothing asked of it is a provider call made to invent a message, so none is made.</summary>
    [Fact]
    public async Task DraftAsync_NeitherAnsweredMessageNorInstruction_DraftsNothingWithoutReachingTheWriter()
    {
        // Arrange
        var writer = WriterReturning(Written("We accept."));
        var drafting = CreateDrafting(SourceReaderReturning(Sources()), writer);

        // Act
        var draft = await drafting.DraftAsync(
            new ReplyDraftRequest { Instruction = "   " },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(draft);
        Assert.False(draft.WasWritten);
        await writer.DidNotReceiveWithAnyArgs().WriteAsync(Arg.Any<ReplyDraftBrief>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The language is the acting user's own, which is the one thing a drafting answering nothing has to go on.</summary>
    [Theory]
    [InlineData(MailUserLanguage.English)]
    [InlineData(MailUserLanguage.Polish)]
    public async Task DraftAsync_AnyDrafting_CarriesTheLanguageTheUserRecordNames(MailUserLanguage language)
    {
        // Arrange
        var writer = WriterReturning(Written("We accept."));
        var drafting = CreateDrafting(SourceReaderReturning(Sources()), writer, language: language);

        // Act
        await drafting.DraftAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        var brief = (ReplyDraftBrief)writer.ReceivedCalls().Single().GetArguments()[0]!;

        Assert.Equal(language, brief.Language);
    }

    private static ReplyDraftRequest Request() => new() { AnsweredEmailId = Answered };

    private static ReplyDraft Written(string body) => ReplyDraft.Written(body, [], []);

    private static EmailAddress Recipient()
    {
        EmailAddress.TryCreate("Karolina", "karolina@example.test", out var karolina);

        return karolina;
    }

    private static ReplyDraftSources Sources(IReadOnlyList<ReplyDraftMessage>? messages = null) =>
        new(
            "The racking quotation",
            messages ??
            [
                new ReplyDraftMessage(
                    Answered,
                    0,
                    "Karolina",
                    new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero),
                    "It is 4 200 zloty."),
            ],
            [new ReplyDraftParticipant(0, Recipient())],
            []);

    private static IReplyDraftSourceReader SourceReaderReturning(ReplyDraftSources? sources)
    {
        var reader = Substitute.For<IReplyDraftSourceReader>();
        reader
            .ReadSourcesAsync(
                Arg.Any<StoredEmailId>(),
                Arg.Any<MailboxScope>(),
                Arg.Any<ReplyDraftBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(sources);

        return reader;
    }

    private static IReplyDraftWriter WriterReturning(ReplyDraft draft)
    {
        var writer = Substitute.For<IReplyDraftWriter>();
        writer.WriteAsync(Arg.Any<ReplyDraftBrief>(), Arg.Any<CancellationToken>()).Returns(draft);

        return writer;
    }

    private static MailReplyDrafting CreateDrafting(
        IReplyDraftSourceReader sourceReader,
        IReplyDraftWriter writer,
        IReadOnlyList<MailAccountId>? servedAccounts = null,
        AccessAuthorization? authorization = null,
        SensitiveContentEgressGuard? egressGuard = null,
        MailUserLanguage language = MailUserLanguage.English,
        bool derivesStyleFromSentMail = true)
    {
        var accounts = servedAccounts ?? [Account];
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([.. accounts.Select(static accountId => SyntheticServedAccount.Of(accountId))]);
        catalog.User.Returns(SyntheticMailUser.Deployment);

        var scopeResolver = new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Mapping(
                [.. accounts.Select(accountId => new MailFolderIdentity(accountId, MailFolderAlias.Create("inbox")))]),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);

        return new MailReplyDrafting(
            sourceReader,
            writer,
            scopeResolver,
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk),
            LanguagesAnswering(language),
            derivesStyleFromSentMail);
    }

    /// <summary>Answers one language for whoever is asked about, which is what a drafting for one user needs.</summary>
    private static IMailUserLanguages LanguagesAnswering(MailUserLanguage language)
    {
        var languages = Substitute.For<IMailUserLanguages>();
        languages.ForUser(Arg.Any<MailUserId>()).Returns(language);

        return languages;
    }
}
