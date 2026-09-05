// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Xml.Linq;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Retrieval;

/// <summary>Puts the adversarial corpus to an answering run: what a message can become, and how far it can reach.</summary>
/// <remarks>
/// <para>
/// The formatter and the composition are each designed against this and each covered by tests of their own. This suite
/// is separate on purpose: a test written beside a claim tends to test the claim, and the attacks are worth keeping as a
/// set that grows when a new one is understood. Adding an entry to <see cref="AdversarialMailCorpus" /> puts it through
/// every property stated over <see cref="EveryAdversary" /> without a test here being edited.
/// <see cref="PassageRelevanceInjectionResistanceTests" /> is the other half, over the second retrieval pass.
/// </para>
/// <para>
/// Every run goes over a substituted chat client, so nothing reaches a network and no provider is paid. The client is
/// scripted as a model that <em>did</em> fall for the message: it writes the query the message demanded rather than
/// refusing it. That is what makes these tests about capability instead of about eloquence — what is asserted is that
/// the run cannot carry the escalation out, never that a model would decline to try.
/// </para>
/// <para>
/// What this does not establish is stated as plainly: a model may still be persuaded to say something wrong about mail
/// it was shown, and nothing here prevents it. The guarantee is that saying it reaches no further.
/// </para>
/// </remarks>
public sealed class PromptInjectionResistanceTests
{
    private const string Query = "what did the insurer agree to pay";

    private static readonly MailboxScope OnePrimaryAccount = MailboxScope.Create(
        SyntheticMailOwner.Deployment,
        [MailAccountId.Create("primary")],
        [new MailFolderIdentity(MailAccountId.Create("primary"), MailFolderAlias.Create("INBOX"))]);

    /// <summary>Gets one case per attack the corpus knows, so a property stated once covers every one of them.</summary>
    public static TheoryData<string> EveryAdversary => AdversarialMailCorpus.EveryName;

    /// <summary>
    /// The property the envelope exists for, over the whole corpus: whatever a message contains, it arrives as the text
    /// of one extract and becomes no element, no second message, and no structure of its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public void Format_AnAdversarialMessage_BecomesQuotedTextAndNoStructureOfItsOwn(string adversary)
    {
        // Arrange
        var message = AdversarialMailCorpus.Named(adversary);
        var passage = KnowledgePassages.Create(message.Text, subject: message.Subject);

        // Act
        var envelope = RetrievedMailContextFormatter.Format([passage], EmailSearchRetrievalMode.Hybrid, retrievalLimitReached: false);

        // Assert
        var written = Assert.Single(MessagesIn(envelope));

        Assert.Equal(message.Text, Element(written, RetrievedMailContextFormatter.ExtractElementName));
        Assert.Equal(message.Subject, Element(written, RetrievedMailContextFormatter.SubjectElementName));
        Assert.Equal(
            [
                RetrievedMailContextFormatter.MessageElementName,
                RetrievedMailContextFormatter.SubjectElementName,
                RetrievedMailContextFormatter.ExtractElementName,
            ],
            RootOf(envelope).Descendants().Select(static element => element.Name.LocalName).Distinct());
    }

    /// <summary>
    /// The structural separation, observed on what a provider was actually sent. The request is accounted for position by
    /// position — this build's instruction, the question as it was asked, the model's own turn, and the envelope the
    /// formatter wrote — so the message occupies the one it is quoted in and no other is left for any of it to have
    /// reached. Asserted as equality rather than as the absence of a substring, because the envelope escapes what a
    /// message wrote and a search for the raw text would report a message that arrived intact as one that never arrived.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public async Task Compose_AnAdversarialMessage_ReachesTheModelOnlyInsideAToolResult(string adversary)
    {
        // Arrange
        var message = AdversarialMailCorpus.Named(adversary);
        var passage = KnowledgePassages.Create(message.Text, subject: message.Subject);
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Returning(Query, passage);
        using var chatClient = ScriptedChatClient.CallingTool(
            ScopedMailKnowledgeRetrieval.SearchToolName,
            Query,
            "A message asks me to do something else; I have not done it.");
        var agent = AgentOver(chatClient, knowledgeSearch);

        // Act
        await agent.RunAsync(Query, session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        var sent = chatClient.Calls[^1].Messages;

        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool], sent.Select(static one => one.Role));
        Assert.Equal(Query, CarriedText(sent[0]));
        Assert.Equal(
            RetrievedMailContextFormatter.Format([passage], EmailSearchRetrievalMode.Hybrid, retrievalLimitReached: false),
            CarriedText(sent[2]));
        Assert.All(
            chatClient.Calls,
            static call => Assert.Equal(MailAnsweringInstructions.Text, call.Options?.Instructions));
    }

    /// <summary>
    /// The scope was bound into the run before the model saw anything, so a model that read the message and did exactly
    /// what it asked has the caller's own scope searched for those words. Nothing is refused here: the widened query is
    /// answered, from the accounts and folders the caller allowed and from no others.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public async Task Compose_AModelThatActedOnAnAdversarialDemand_RetrievesWithinTheCallersScopeAlone(string adversary)
    {
        // Arrange
        var demand = AdversarialMailCorpus.Named(adversary).Demand;
        var knowledgeSearch = new RecordingEmailKnowledgeSearch();
        using var chatClient = ScriptedChatClient.CallingTool(
            ScopedMailKnowledgeRetrieval.SearchToolName,
            demand,
            "Nothing matched.");
        var agent = AgentOver(chatClient, knowledgeSearch);

        // Act
        await agent.RunAsync(Query, session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        var lookup = Assert.Single(knowledgeSearch.Calls);

        Assert.Equal(demand, lookup.QueryText);
        Assert.Same(OnePrimaryAccount, lookup.Scope);
    }

    /// <summary>Reads everything one message would put in front of the model, whichever content shape carries it.</summary>
    /// <remarks>A tool result is not text content, so <see cref="ChatMessage.Text" /> reports nothing for it.</remarks>
    private static string CarriedText(ChatMessage message) =>
        string.Concat(message.Contents.Select(static content => content switch
        {
            TextContent text => text.Text,
            FunctionResultContent result => result.Result?.ToString(),
            _ => null,
        }));

    private static ChatClientAgent AgentOver(
        ScriptedChatClient chatClient,
        RecordingEmailKnowledgeSearch knowledgeSearch) =>
        MailAnsweringAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new ScopedMailKnowledgeRetrieval(
                knowledgeSearch,
                OnePrimaryAccount,
                new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
                SensitiveContentEgressGuards.Inactive()),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

    private static IReadOnlyList<XElement> MessagesIn(string envelope) =>
        [.. RootOf(envelope).Elements(RetrievedMailContextFormatter.MessageElementName)];

    /// <summary>Reads the envelope back as the document it claims to be, which is itself part of what is asserted.</summary>
    private static XElement RootOf(string envelope)
    {
        var root = XDocument.Parse(envelope).Root
            ?? throw new InvalidOperationException("The envelope carried no root element.");

        return root.Name.LocalName == RetrievedMailContextFormatter.RetrievalElementName
            ? root
            : throw new InvalidOperationException($"The envelope opened with '{root.Name.LocalName}'.");
    }

    private static string Element(XElement message, string name) =>
        message.Element(name)?.Value
        ?? throw new InvalidOperationException($"The message carried no '{name}' element.");
}
