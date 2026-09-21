// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

/// <summary>Covers what a question may be asked about, and that a kind and its object cannot disagree.</summary>
/// <remarks>
/// The scope is what narrows a run's reading, so a pairing nothing checked would be a question answered about something
/// other than what the person opened the agent from — and a scope naming nothing would be one answered about the whole
/// mailbox without anybody having asked for that.
/// </remarks>
public sealed class AgentMessageScopeTests
{
    [Fact]
    public void Mailbox_AQuestionAskedWithNothingNamed_NamesNoObject()
    {
        // Act
        var scope = AgentMessageScope.Mailbox();

        // Assert
        Assert.Equal(AgentScopeKind.Mailbox, scope.Kind);
        Assert.Null(scope.Subject);
    }

    [Fact]
    public void Thread_AQuestionAskedFromAThread_NamesThatThread()
    {
        // Arrange
        var thread = EmailThreadId.Create(Guid.NewGuid());

        // Act
        var scope = AgentMessageScope.Thread(thread);

        // Assert
        Assert.Equal(AgentScopeKind.Thread, scope.Kind);
        Assert.Equal(thread.Value, scope.Subject);
    }

    [Fact]
    public void CalendarEvent_AQuestionAskedFromAnEvent_NamesThatEvent()
    {
        // Arrange
        var entry = CalendarEventId.Create(Guid.NewGuid());

        // Act
        var scope = AgentMessageScope.CalendarEvent(entry);

        // Assert
        Assert.Equal(AgentScopeKind.CalendarEvent, scope.Kind);
        Assert.Equal(entry.Value, scope.Subject);
    }

    [Fact]
    public void DiscoveryRun_AQuestionCarryingOnFromARun_NamesThatRun()
    {
        // Arrange
        var run = DiscoveryRunId.New();

        // Act
        var scope = AgentMessageScope.DiscoveryRun(run);

        // Assert
        Assert.Equal(AgentScopeKind.DiscoveryRun, scope.Kind);
        Assert.Equal(run.Value, scope.Subject);
    }

    /// <summary>A question about everything that named one thing would be a narrowing nothing asked for.</summary>
    [Fact]
    public void Constructor_AMailboxScopeNamingAnObject_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentMessageScope(AgentScopeKind.Mailbox, Guid.NewGuid()));
    }

    [Fact]
    public void Constructor_AScopeOfAKindThatNamesSomethingNamingNothing_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentMessageScope(AgentScopeKind.Thread, subject: null));
    }

    [Fact]
    public void Constructor_AScopeNamingTheEmptyIdentifier_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AgentMessageScope(AgentScopeKind.CalendarEvent, Guid.Empty));
    }

    [Fact]
    public void Constructor_AKindTheSetDoesNotDeclare_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentMessageScope((AgentScopeKind)99, Guid.NewGuid()));
    }
}
