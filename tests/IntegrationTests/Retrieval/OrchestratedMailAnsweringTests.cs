// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Xml.Linq;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Retrieval;

/// <summary>
/// Puts questions to <c>ask_mail</c> over a real mailbox and asks whether asking is ever a worse way to find something
/// than searching.
/// </summary>
/// <remarks>
/// <para>
/// This is the reproduction issue 639 asked for, kept as tests rather than as a one-off measurement. Every run below
/// goes through the deployment's own path — PostgreSQL, both rankings, the search use case, the retrieval port, the tool
/// the run offers, the run ledger, and the published result — with one substitute: the chat provider, which follows a
/// written script. What a model would decide is the one thing a test states rather than discovers, and stating it is
/// what makes the retrieval underneath measurable at all.
/// </para>
/// <para>
/// It belongs here rather than in the unit suite because the claim is about what the two tools <em>reach</em>. A
/// substitute is where the two rankings are assumed to agree about which message they are ranking, and the whole gap
/// this class exists to close lives in what a real query over a real index returns.
/// </para>
/// <para>
/// The mailbox is synthetic throughout: the addresses are in the reserved <c>.test</c> domain and every body is
/// generated text. No question, subject, address, or extract here comes from a real mailbox.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailAnsweringTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The folder every question below is scoped to, owned by this class alone.</summary>
    private const string FolderAlias = "ask-mail";

    /// <summary>A second folder, seeded so that a scope naming the first can be shown to exclude something real.</summary>
    private const string OtherFolderAlias = "ask-mail-elsewhere";

    /// <summary>The word every seeded body of the first folder carries, so one query reaches the whole corpus.</summary>
    private const string CorpusTerm = "settlement";

    private const string AnnaAddress = "anna@mailfathom.test";
    private const string BrunoAddress = "bruno@mailfathom.test";
    private const string CarlaAddress = "carla@mailfathom.test";

    private const string SettledSubject = "ask-mail-settled";
    private const string OpenedSubject = "ask-mail-opened";
    private const string DiscussedSubject = "ask-mail-discussed";
    private const string LaterSubject = "ask-mail-later";
    private const string ElsewhereSubject = "ask-mail-elsewhere-message";

    private static readonly DateTimeOffset FirstWeek = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondWeek = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The defect stated as a test. A question a caller could have answered by searching must not come back weaker from
    /// the run: every message one <c>search_emails</c> window reaches is a message the run cites, because both read the
    /// same index through the same use case and the run's lookup is now able to ask for the same thing.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_AQuestionSearchAlsoAnswers_CitesEveryMessageTheSearchWindowReached()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup(CorpusTerm),
            "The settlement figure is stated in the messages cited.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        await SeedAsync(services, cancellationToken);

        // Act
        var searched = await SearchAsync(services, CorpusTerm, cancellationToken);
        var answer = await AskAsync(services, "what was the settlement", cancellationToken);

        // Assert
        Assert.NotEmpty(searched);
        Assert.Equal(Ordered(searched), Cited(answer));
    }

    /// <summary>
    /// A question about one person's mail is answered by selecting that person's mail. Bruno's message carries the term
    /// far more often and therefore outranks Anna's, so an unnarrowed lookup would lead with it; the narrowed lookup
    /// reaches Anna and nobody else, which is the whole point of publishing the filter.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_AQuestionAboutOnePersonsMail_DrawsOnThatSenderAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup(CorpusTerm, senderAddress: AnnaAddress),
            "Anna wrote about the settlement.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "what did Anna say about the settlement", cancellationToken);

        // Assert
        Assert.Equal(
            Ordered([seeded.Settled, seeded.Opened]),
            Cited(answer));
    }

    /// <summary>A question about one period narrows on the period rather than hoping a date outranks the prose around it.</summary>
    [Fact]
    public async Task AskMailAsync_AQuestionAboutOnePeriod_DrawsOnMailReceivedInIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup(
                CorpusTerm,
                receivedOnOrAfter: SecondWeek.AddDays(-1),
                receivedBefore: SecondWeek.AddDays(1)),
            "One message arrived in that week.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "what arrived about the settlement that week", cancellationToken);

        // Assert
        Assert.Equal(seeded.Later, Assert.Single(answer.Citations).StoredEmailId);
    }

    /// <summary>A subject a caller would recognize is a filter over the stored subject rather than words competing in the ranking.</summary>
    [Fact]
    public async Task AskMailAsync_AQuestionNamingASubject_DrawsOnTheMailCarryingIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup(CorpusTerm, subjectFragment: DiscussedSubject),
            "The discussion thread mentions it.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "what does the discussion thread say", cancellationToken);

        // Assert
        Assert.Equal(seeded.Discussed, Assert.Single(answer.Citations).StoredEmailId);
    }

    /// <summary>
    /// The shape a question spanning a thread takes: several lookups, each narrowing differently, and an answer resting
    /// on what all of them together reached.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_AQuestionSpanningSeveralMessages_CitesWhatEveryLookupReached()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUpSeveralTimes(
            [
                ScriptedAnsweringChatClient.Lookup(CorpusTerm, subjectFragment: OpenedSubject),
                ScriptedAnsweringChatClient.Lookup(CorpusTerm, subjectFragment: SettledSubject),
            ],
            "The claim was opened first and settled afterwards.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "how did the claim begin and end", cancellationToken);

        // Assert
        Assert.Equal(
            Ordered([seeded.Opened, seeded.Settled]),
            Cited(answer));
    }

    /// <summary>
    /// A run makes several lookups and one message can answer more than one of them. A reader given a list of sources
    /// wants the messages rather than the number of times each was found.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_OneMessageReachedByTwoLookups_IsCitedOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var lookup = ScriptedAnsweringChatClient.Lookup(CorpusTerm, subjectFragment: SettledSubject);
        using var chatClient = ScriptedAnsweringChatClient.LookingUpSeveralTimes(
            [lookup, lookup],
            "The settlement is stated once.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "what was settled", cancellationToken);

        // Assert
        Assert.Equal(seeded.Settled, Assert.Single(answer.Citations).StoredEmailId);
    }

    /// <summary>A question whose mail does not answer it is answered by saying so, and cites nothing rather than the nearest thing.</summary>
    [Fact]
    public async Task AskMailAsync_AQuestionTheMailboxDoesNotAnswer_AnswersWithoutCitingAnything()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup("custody", senderAddress: CarlaAddress),
            "The retrieved mail does not answer this question.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "who has custody of the archive", cancellationToken);

        // Assert
        Assert.Empty(answer.Citations);
        Assert.Equal("The retrieved mail does not answer this question.", answer.AnswerText);
    }

    /// <summary>
    /// A filter the search use case refuses ends the lookup and not the run. The model is told which argument was
    /// refused, writes another one, and the question is answered — which is the whole reason a refusal is a document
    /// rather than an empty envelope or a thrown failure.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_ALookupTheSearchRefuses_TellsTheModelAndLetsItLookAgain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUpSeveralTimes(
            [
                ScriptedAnsweringChatClient.Lookup(CorpusTerm, senderAddress: "anna, the insurer"),
                ScriptedAnsweringChatClient.Lookup(CorpusTerm, senderAddress: AnnaAddress),
            ],
            "Anna wrote about the settlement.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "what did Anna say about the settlement", cancellationToken);

        // Assert
        var refusal = ToolResults(chatClient)[0];

        Assert.Equal(RetrievedMailContextFormatter.RefusalElementName, RootOf(refusal).Name.LocalName);
        Assert.Equal(
            "sender address",
            RootOf(refusal).Attribute(RetrievedMailContextFormatter.RefusedFilterAttributeName)?.Value);

        // The run recovered: the corrected lookup reached the mail and the answer rests on it.
        Assert.Equal(
            Ordered([seeded.Settled, seeded.Opened]),
            Cited(answer));
    }

    /// <summary>
    /// What the model is actually handed: the envelope naming which ranking answered and one element per message, each
    /// carrying an identifier this database resolves. A citation nobody could follow would be worth nothing.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_ALookup_HandsTheModelAnEnvelopeNamingTheRankingAndTheMail()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup(CorpusTerm, subjectFragment: SettledSubject),
            "The settlement figure is 4200.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // Act
        await AskAsync(services, "what was the settlement", cancellationToken);

        // Assert
        var envelope = RootOf(Assert.Single(ToolResults(chatClient)));

        // Hybrid, because this instance embeds: the attribute describes what happened to this lookup rather than what
        // the deployment is configured for, which is why it is written per lookup at all.
        Assert.Equal(
            RetrievedMailContextFormatter.HybridRetrievalMode,
            envelope.Attribute(RetrievedMailContextFormatter.RetrievalModeAttributeName)?.Value);
        Assert.Equal(
            [seeded.Settled.ToString()],
            envelope.Elements(RetrievedMailContextFormatter.MessageElementName)
                .Select(message => message.Attribute(RetrievedMailContextFormatter.MessageIdAttributeName)?.Value));
    }

    /// <summary>
    /// The scope was bound into the run before the model saw anything, so a model that writes a query naming another
    /// folder has the caller's own folder searched for those words. The other folder is seeded and reachable, which is
    /// what makes its absence a fact rather than an empty mailbox.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_AModelWritingAQueryNamingAnotherFolder_DrawsOnTheCallersFolderAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.LookingUp(
            ScriptedAnsweringChatClient.Lookup($"{CorpusTerm} in {OtherFolderAlias}"),
            "Only this folder was searched.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        var seeded = await SeedAsync(services, cancellationToken);

        // The control: the message the query names is real and this deployment can reach it, from the folder it is in.
        var elsewhere = await SearchAsync(services, CorpusTerm, cancellationToken, OtherFolderAlias);
        Assert.Equal(seeded.Elsewhere, Assert.Single(elsewhere));

        // Act
        var answer = await AskAsync(services, "what is in the other folder", cancellationToken);

        // Assert
        Assert.DoesNotContain(seeded.Elsewhere, answer.Citations.Select(citation => citation.StoredEmailId));
        Assert.All(
            answer.Citations,
            citation => Assert.Equal(MailFolderAlias.Create(FolderAlias), citation.FolderAlias));
    }

    /// <summary>A question that needs no mail costs one provider call and reads no mailbox, which is what on-demand retrieval buys.</summary>
    [Fact]
    public async Task AskMailAsync_AQuestionNeedingNoMail_ReadsTheMailboxNotAtAll()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var chatClient = ScriptedAnsweringChatClient.Answering("I answer questions about your mailbox.");

        await using var services = await this.StartAnsweringAsync(chatClient, cancellationToken);
        await SeedAsync(services, cancellationToken);

        // Act
        var answer = await AskAsync(services, "what can you do", cancellationToken);

        // Assert
        Assert.Empty(answer.Citations);
        Assert.Empty(ToolResults(chatClient));
        Assert.Single(chatClient.Conversations);
    }

    /// <summary>
    /// The absence this whole class is read against: a deployment that declared no chat endpoint refuses a question
    /// rather than answering one, and refuses it from the same use case every test above answers through.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_ADeploymentThatDeclaredNoChatEndpoint_RefusesTheQuestion()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await SeedAsync(services, cancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<MailAnsweringUnavailableException>(
            () => AskAsync(services, "what was the settlement", cancellationToken));
    }

    /// <summary>Starts the deployment these tests ask questions of, which is the shipped one plus a scripted provider.</summary>
    private Task<OrchestratedMailFathomServices> StartAnsweringAsync(
        IChatClient chatClient,
        CancellationToken cancellationToken) => OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            answeringChatClient: chatClient);

    /// <summary>Asks one question of this class's folder, through the use case the MCP tool calls.</summary>
    private static Task<AskMailResult> AskAsync(
        OrchestratedMailFathomServices services,
        string question,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxQuestionReader>().AnswerQuestionAsync(
                new AskMailRequest
                {
                    QuestionText = question,
                    Accounts = [MailAccountSelector.For(SyntheticMailAccount.AccountId)],
                    Folders = [MailFolderReference.ToAlias(MailFolderAlias.Create(FolderAlias))],
                },
                token),
            cancellationToken);

    /// <summary>Searches the same folder for the same text, which is what a run's reach is measured against.</summary>
    private static Task<IReadOnlyList<StoredEmailId>> SearchAsync(
        OrchestratedMailFathomServices services,
        string queryText,
        CancellationToken cancellationToken,
        string folderAlias = FolderAlias) => services.InScopeAsync(
            async (scope, token) =>
            {
                var result = await scope.GetRequiredService<MailboxSearchReader>().SearchEmailsAsync(
                    new SearchEmailsRequest
                    {
                        QueryText = queryText,
                        Accounts = [MailAccountSelector.For(SyntheticMailAccount.AccountId)],
                        Folders = [MailFolderReference.ToAlias(MailFolderAlias.Create(folderAlias))],
                    },
                    token);

                return (IReadOnlyList<StoredEmailId>)
                    [.. result.Matches.Select(match => match.Summary.StoredEmailId)];
            },
            cancellationToken);

    /// <summary>Reads every document the run's lookups handed the model, in the order the run received them.</summary>
    /// <remarks>
    /// Taken from the last conversation, which is the only one holding every tool result: the agent appends each result
    /// to the conversation it sends next, so the final turn carries the whole of what the mailbox gave the model.
    /// </remarks>
    private static IReadOnlyList<string> ToolResults(ScriptedAnsweringChatClient chatClient) =>
    [
        .. chatClient.Conversations[^1]
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? string.Empty),
    ];

    /// <summary>Names the emails one answer cited, in an order two runs agree on.</summary>
    /// <remarks>
    /// Read as the identifiers underneath rather than as the identity type, because the identity is a record struct with
    /// no ordering of its own: sorting a sequence of them reaches <c>Comparer&lt;T&gt;.Default</c> and fails at run time
    /// rather than at compile time.
    /// </remarks>
    private static Guid[] Cited(AskMailResult answer) =>
        Ordered([.. answer.Citations.Select(citation => citation.StoredEmailId)]);

    private static Guid[] Ordered(IReadOnlyList<StoredEmailId> storedEmailIds) =>
        [.. storedEmailIds.Select(storedEmailId => storedEmailId.Value).Order()];

    private static XElement RootOf(string document) =>
        XDocument.Parse(document).Root
        ?? throw new InvalidOperationException("The tool answered with no root element.");

    /// <summary>
    /// Ensures this class's two folders hold their corpus, embedded under the active profile, and names each message.
    /// </summary>
    /// <remarks>
    /// Idempotent for the reason every seeding in this suite is: the binding store recognizes its own row and the
    /// metadata repository upserts by occurrence identity, so whichever test runs first writes the mailbox and the rest
    /// find it. Every body carries the corpus term once except the one that repeats it, which is what makes the ranking
    /// between two of them predictable without asserting on a rank.
    /// </remarks>
    private static async Task<SeededMailbox> SeedAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var otherBinding = await OrchestratedFolderBinding.CommitAsync(services, OtherFolderAlias, cancellationToken);
        await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        var settled = await StoreAsync(
            services,
            binding,
            uid: 9801,
            SettledSubject,
            SyntheticEmail.BodyTextContaining(CorpusTerm, wordCount: 30),
            AnnaAddress,
            FirstWeek.AddDays(1),
            cancellationToken);
        var opened = await StoreAsync(
            services,
            binding,
            uid: 9802,
            OpenedSubject,
            SyntheticEmail.BodyTextContaining(CorpusTerm, wordCount: 30),
            AnnaAddress,
            FirstWeek,
            cancellationToken);
        var discussed = await StoreAsync(
            services,
            binding,
            uid: 9803,
            DiscussedSubject,
            RepeatedTerm(CorpusTerm, times: 12),
            BrunoAddress,
            FirstWeek.AddDays(2),
            cancellationToken);
        var later = await StoreAsync(
            services,
            binding,
            uid: 9804,
            LaterSubject,
            SyntheticEmail.BodyTextContaining(CorpusTerm, wordCount: 30),
            CarlaAddress,
            SecondWeek,
            cancellationToken);
        var elsewhere = await StoreAsync(
            services,
            otherBinding,
            uid: 9805,
            ElsewhereSubject,
            SyntheticEmail.BodyTextContaining(CorpusTerm, wordCount: 30),
            CarlaAddress,
            FirstWeek,
            cancellationToken);

        foreach (var storedEmailId in new[] { settled, opened, discussed, later, elsewhere })
        {
            await EmbedAsync(services, storedEmailId, cancellationToken);
        }

        return new SeededMailbox(settled, opened, discussed, later, elsewhere);
    }

    /// <summary>Builds a body carrying the corpus term several times, so it outranks the bodies carrying it once.</summary>
    private static string RepeatedTerm(string term, int times) =>
        string.Join(' ', Enumerable.Repeat(term, times).Concat(["filler0", "filler1", "filler2"]));

    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        string subject,
        string bodyText,
        string senderAddress,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    SyntheticEmail.ExtractionFrom(
                        occurrenceId,
                        subject,
                        bodyText,
                        senderAddress,
                        receivedAt,
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId;
    }

    /// <summary>Embeds every passage of one message under the active profile, which a repeat run finds nothing to do for.</summary>
    private static Task<StoredEmailEmbeddingRun> EmbedAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var serving = await scope.GetRequiredService<IActiveEmbeddingProfileReader>()
                    .FindActiveProfileAsync(token);

                return await scope.GetRequiredService<StoredEmailEmbeddingGenerator>().EmbedAsync(
                    storedEmailId,
                    Assert.IsType<RegisteredEmbeddingProfile>(serving),
                    token);
            },
            cancellationToken);

    /// <summary>The corpus every question above is put to, named by what each message is for.</summary>
    private sealed record SeededMailbox(
        StoredEmailId Settled,
        StoredEmailId Opened,
        StoredEmailId Discussed,
        StoredEmailId Later,
        StoredEmailId Elsewhere);
}
