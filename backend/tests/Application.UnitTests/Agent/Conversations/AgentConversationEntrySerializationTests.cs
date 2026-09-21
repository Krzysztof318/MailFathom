// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Conversations;

/// <summary>Covers the shape a conversation's entry is stored as, which outlives the process that wrote it.</summary>
/// <remarks>
/// A conversation is durable, so this document is read back by builds that do not exist yet and written by builds an
/// older one has to read. What breaks silently here is a discriminator: an entry read back as its base type renders as
/// nothing on a screen and fails no assertion about the code around it.
/// </remarks>
public sealed class AgentConversationEntrySerializationTests
{
    [Fact]
    public void Serialize_EveryKindOfEntry_ReadsBackAsTheKindItWasWrittenAs()
    {
        // Arrange
        var message = AgentMessageId.New();

        AgentConversationEntry[] entries =
        [
            AgentConversationExample.Question(message, "Where did we land on the price?"),
            new AgentAnswerStarted(message),
            new AgentStatusReported(message, PresentationText.Create("reading the attachments")),
            new AgentCitationDeclared(message, PresentationPlanExample.Citations()[0]),
            new AgentBlockComposed(message, AgentConversationExample.Reading()),
            new AgentActionProposed(message, AgentConversationExample.Actionable()),
            new AgentAnswerEnded(message, AgentAnswerOutcome.Stopped),
            new AgentProposalResolved(3, AgentProposalState.Accepted),
        ];

        // Act
        var read = entries.Select(RoundTrip).ToArray();

        // Assert
        Assert.Equal(
            entries.Select(entry => entry.GetType()),
            read.Select(entry => entry.GetType()));
        Assert.Equal(
            entries.Select(entry => entry.EntryName),
            read.Select(entry => entry.EntryName));
    }

    /// <summary>A block is itself polymorphic, so an entry carrying one nests two discriminators.</summary>
    [Fact]
    public void Serialize_AnEntryCarryingABlock_ReadsTheBlockBackAsItsOwnType()
    {
        // Arrange
        var composed = AgentConversationExample.Reading();
        var entry = new AgentBlockComposed(AgentMessageId.New(), composed);

        // Act
        var read = Assert.IsType<AgentBlockComposed>(RoundTrip(entry));

        // Assert
        Assert.Equal(composed.GetType(), read.Block.GetType());
        Assert.Equal(composed.Type.Identity, read.Block.Type.Identity);
    }

    [Fact]
    public void Serialize_AQuestionAskedAboutAThread_ReadsItsScopeBack()
    {
        // Arrange
        var thread = EmailThreadId.Create(Guid.NewGuid());
        var entry = new AgentMessageWritten(
            AgentMessageId.New(),
            AgentMessageAuthor.Person,
            PresentationText.Create("What is outstanding here?"),
            AgentMessageScope.Thread(thread));

        // Act
        var read = Assert.IsType<AgentMessageWritten>(RoundTrip(entry));

        // Assert
        Assert.Equal(AgentScopeKind.Thread, read.Scope?.Kind);
        Assert.Equal(thread.Value, read.Scope?.Subject);
    }

    /// <summary>The store stamps a place onto an entry it has just read, through the base type it holds it as.</summary>
    /// <remarks>
    /// A place is derived where the row is written, so an entry is serialized before there is one to serialize and the
    /// stamping happens on the way back. It is done through <see cref="AgentConversationEntry" /> rather than through
    /// the kind, so this is where a copy that quietly produced a base entry would be found — and a conversation read
    /// back as a list of base entries renders as nothing and fails no assertion about the code around it.
    /// </remarks>
    [Fact]
    public void Sequence_AnEntryTheStoreStampsThroughTheBaseType_KeepsTheKindItWasWrittenAs()
    {
        // Arrange
        AgentConversationEntry entry = new AgentBlockComposed(
            AgentMessageId.New(),
            AgentConversationExample.Reading());

        // Act
        var placed = RoundTrip(entry) with { ConversationId = AgentConversationExample.Conversation, Sequence = 12 };

        // Assert
        Assert.IsType<AgentBlockComposed>(placed);
        Assert.Equal(12, placed.Sequence);
        Assert.Equal(AgentConversationExample.Conversation, placed.ConversationId);
    }

    private static AgentConversationEntry RoundTrip(AgentConversationEntry entry)
    {
        var written = JsonSerializer.Serialize(entry, AgentConversationEntryJsonContext.Default.AgentConversationEntry);

        return JsonSerializer.Deserialize(written, AgentConversationEntryJsonContext.Default.AgentConversationEntry)!;
    }
}
