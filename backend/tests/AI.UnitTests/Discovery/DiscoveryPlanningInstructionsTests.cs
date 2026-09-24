// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what the planning agent is told, and what of the mailbox reaches it.</summary>
public sealed class DiscoveryPlanningInstructionsTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

    /// <summary>The instant every question is asked at, which is the anchor the turn states.</summary>
    private static readonly DateTimeOffset AskedAt = new(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2));

    /// <summary>Every name the reading parses is a name the instruction offered, or a model is being asked to guess.</summary>
    [Fact]
    public void Text_TheInstruction_NamesEveryIntentTheReadingCanRead()
    {
        // Act
        var text = DiscoveryPlanningInstructions.Text;

        // Assert
        Assert.All(
            DiscoveryIntent.All,
            intent => Assert.Contains(intent.Identity, text, StringComparison.Ordinal));
    }

    /// <summary>Every word of a lookup is required and none is stemmed, which a planner writing the question itself as the lookup does not know.</summary>
    [Fact]
    public void Text_TheInstruction_StatesHowTheWordsOfALookupAreMatched()
    {
        // Act
        var text = DiscoveryPlanningInstructions.Text;

        // Assert
        Assert.Contains(EmailSearchQueryText.MatchingDescription, text, StringComparison.Ordinal);
    }

    /// <summary>A plan is run without its author seeing a result, so one long lookup that misses is the whole plan missing.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForSeveralShortLookupsBecauseNoneIsRetried()
    {
        // Act
        var text = string.Join(' ', DiscoveryPlanningInstructions.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("write several lookups rather than one", text, StringComparison.Ordinal);
        Assert.Contains("the most distinctive first and a broader one after it", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every word of a lookup has to be in the message and the extract is cut around those words, so a lookup copying the
    /// question's own wording misses the message and one naming only the matter hands over an extract without the answer.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_AsksForALookupWithoutTheQuestionsWordingAndOneBesideTheAnswer()
    {
        // Act
        var text = DiscoveryPlanningInstructions.Text.ReplaceLineEndings(" ");

        // Assert
        Assert.Contains("leave such words out of at least one lookup", text, StringComparison.Ordinal);
        Assert.Contains("let one lookup also carry the word the answer itself stands beside", text, StringComparison.Ordinal);
    }

    /// <summary>The agent is never asked for a block type, because the composition is derived from the intent in code.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForNoPresentation()
    {
        // Act
        var text = DiscoveryPlanningInstructions.Text;

        // Assert
        Assert.DoesNotContain("block", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A question about a selection is planned differently from one about a mailbox, so the count is stated.</summary>
    [Fact]
    public void ComposePlanningTurn_AQuestionAboutSelectedMessages_StatesHowManyWereSelected()
    {
        // Arrange
        var scope = Scope().NarrowedToEmails([Email("11111111-1111-1111-1111-111111111111")]);

        // Act
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", scope, AskedAt, Bounds);

        // Assert
        Assert.Contains("1 individually selected messages", turn, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposePlanningTurn_AQuestionAboutOneConversation_SaysSo()
    {
        // Arrange
        var scope = Scope().NarrowedToThread(
            EmailThreadId.Create(new Guid("22222222-2222-2222-2222-222222222222")));

        // Act
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", scope, AskedAt, Bounds);

        // Assert
        Assert.Contains("one conversation", turn, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposePlanningTurn_AQuestionAboutTheWholeMailbox_SaysHowManyAccountsItReaches()
    {
        // Act
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", Scope(), AskedAt, Bounds);

        // Assert
        Assert.Contains("every folder of 1 mail accounts", turn, StringComparison.Ordinal);
    }

    /// <summary>Nothing of the mailbox leaves this deployment to derive a plan, so no identifier is in the turn.</summary>
    [Fact]
    public void ComposePlanningTurn_AnyScope_NamesNoAccountNoFolderAndNoMessage()
    {
        // Arrange
        var email = Email("33333333-3333-3333-3333-333333333333");
        var scope = MailboxScope
            .Create([Primary], [new MailFolderIdentity(Primary, MailFolderAlias.Create("ARCHIVE"))])
            .NarrowedToEmails([email]);

        // Act
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", scope, AskedAt, Bounds);

        // Assert
        Assert.DoesNotContain(Primary.Value, turn, StringComparison.Ordinal);
        Assert.DoesNotContain("ARCHIVE", turn, StringComparison.Ordinal);
        Assert.DoesNotContain(email.Value.ToString(), turn, StringComparison.Ordinal);
    }

    private static MailboxScope Scope() =>
        MailboxScope.Create([Primary], []);

    private static StoredEmailId Email(string identity) => StoredEmailId.Create(new Guid(identity));
}
