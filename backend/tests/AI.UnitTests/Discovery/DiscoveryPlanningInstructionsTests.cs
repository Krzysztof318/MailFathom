// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what the planning agent is told, and what of the mailbox reaches it.</summary>
public sealed class DiscoveryPlanningInstructionsTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

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
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", scope, Bounds);

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
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", scope, Bounds);

        // Assert
        Assert.Contains("one conversation", turn, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposePlanningTurn_AQuestionAboutTheWholeMailbox_SaysHowManyAccountsItReaches()
    {
        // Act
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", Scope(), Bounds);

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
            .Create(SyntheticMailOwner.Deployment, [Primary], [new MailFolderIdentity(Primary, MailFolderAlias.Create("ARCHIVE"))])
            .NarrowedToEmails([email]);

        // Act
        var turn = DiscoveryPlanningInstructions.ComposePlanningTurn("which quote", scope, Bounds);

        // Assert
        Assert.DoesNotContain(Primary.Value, turn, StringComparison.Ordinal);
        Assert.DoesNotContain("ARCHIVE", turn, StringComparison.Ordinal);
        Assert.DoesNotContain(email.Value.ToString(), turn, StringComparison.Ordinal);
    }

    private static MailboxScope Scope() =>
        MailboxScope.Create(SyntheticMailOwner.Deployment, [Primary], []);

    private static StoredEmailId Email(string identity) => StoredEmailId.Create(new Guid(identity));
}
