// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Domain.Access;
using MailFathom.Domain.Tasks;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Proves the deployment's own readers answer over the corpus, so a tool that finds nothing in a run is the model's doing rather than the harness's.</summary>
public sealed class CorpusReadersTests
{
    private static AgentConversationReaders Readers() =>
        CorpusReaders.For(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead, MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend),
            new CorpusKnowledgeSearch(AgentConversationScenario.Mailbox));

    [Fact]
    public async Task ContentReader_AConversationsThread_ReadsEveryMessageInItRenderedFromItsMime()
    {
        // Arrange
        var thread = CorpusReaders.ThreadOf(PersonalAgenda.ArchiveBoxes);

        // Act
        var read = await Readers().ContentReader.ReadContentAsync(GetEmailContentRequest.CreateForThread(thread), TestContext.Current.CancellationToken);

        // Assert
        var bodies = read.Emails.Select(static outcome => outcome.Content?.Body.PlainText.Text ?? string.Empty).ToArray();

        Assert.Equal(PersonalAgenda.ArchiveBoxes.Count, bodies.Length);
        Assert.Contains(bodies, static body => body.Contains("Emil", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateBrowser_AConversationWithADerivedState_ReadsItsEntries()
    {
        // Act
        var state = await Readers().StateBrowser.ReadStateAsync(CorpusReaders.ThreadOf(PersonalAgenda.KestrelQuayMove), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, state?.Entries.Count);
    }

    [Fact]
    public async Task Calendar_WednesdaysWindow_ReadsWhatIsScheduledThatDay()
    {
        // Act
        var events = await Readers().Calendar.ReadWindowAsync(
            new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero),
            origin: null,
            count: 50,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Lunch with Ada Zielinska", "Budget review with finance"], events?.Select(static entry => entry.Title.Value));
    }

    [Fact]
    public async Task Tasks_TheTasksMailProposed_ReadsTheOnesReadOutOfMail()
    {
        // Act
        var page = await Readers().Tasks.ReadPageAsync(PersonalTaskOrigin.Proposed, pageSize: 50, cursor: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Reply to Quayside Supplies about the A4 paper delivery", PersonalAgenda.HostileTaskTitle],
            page?.Tasks.Select(static task => task.Title));
    }

    [Fact]
    public async Task ResponseAuthoring_AReplyToACorpusMessage_IsAddressedToItsSender()
    {
        // Arrange
        var answered = PersonalAgenda.ArchiveBoxes[2];

        // Act
        var authored = await Readers().ResponseAuthoring.AuthorAsync(
            new AuthoredResponseRequest
            {
                AnsweredEmailId = answered.Id,
                Act = AuthoredResponseAct.Reply,
                PlainTextBody = "Thank you.",
                Recipients = [],
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([answered.Sender], authored.Email?.Recipients.Select(static recipient => recipient.Address));
    }

    [Fact]
    public void ThreadOf_EveryConversation_NamesAThreadNoOtherConversationOrMessageCarries()
    {
        // Arrange
        IReadOnlyList<IReadOnlyList<CorpusMessage>> exchanges = [.. CorpusMessage.Exchanges, .. HostileMail.Exchanges, .. PolishCorpus.Exchanges];

        // Act
        var threads = exchanges.Select(static exchange => CorpusReaders.ThreadOf(exchange).Value).ToHashSet();

        // Assert
        Assert.Equal(
            (exchanges.Count, false),
            (threads.Count, exchanges.SelectMany(static exchange => exchange).Any(message => threads.Contains(message.Id.Value))));
    }
}
