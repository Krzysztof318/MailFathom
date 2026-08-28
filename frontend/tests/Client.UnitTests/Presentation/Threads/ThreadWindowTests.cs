// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Threads;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Threads;

/// <summary>What one conversation accumulates as it is paged, and what a page arriving late is held against.</summary>
public sealed class ThreadWindowTests
{
    /// <summary>Opening a conversation establishes what the deployment said about the whole of it, not about the page.</summary>
    [Fact]
    public void Opening_APageADeploymentServed_KeepsWhatItSaidAboutTheWholeConversation()
    {
        // Arrange
        var page = MailThreads.Page(
            [MailThreads.Message(1), MailThreads.Message(2)],
            messageCount: 9,
            moreMessagesNotAssembled: true,
            moreParticipantsNotNamed: true,
            nextCursor: "after-2");

        // Act
        var window = ThreadWindow.Opening(new ThreadOpening(MailThreads.Identity, MailMessages.Identity(2)), page);

        // Assert
        Assert.True(window.IsOpen);
        Assert.Equal(MailThreads.Identity, window.ThreadId);
        Assert.Equal(MailMessages.Identity(2), window.OpenedAt);
        Assert.Equal(9, window.MessageCount);
        Assert.True(window.MoreMessagesNotAssembled);
        Assert.True(window.MoreParticipantsNotNamed);
        Assert.True(window.HasMore);
        Assert.Equal(2, window.Messages.Count);
    }

    /// <summary>A conversation grows forwards, so a page is taken onto the end of what has been read.</summary>
    [Fact]
    public void Extended_AFollowingPage_TakesItOntoTheEndOfWhatWasRead()
    {
        // Arrange
        var window = ThreadWindow.Opening(
            new ThreadOpening(MailThreads.Identity, AtMessage: null),
            MailThreads.Page([MailThreads.Message(1)], nextCursor: "after-1"));

        // Act
        var extended = window.Extended(MailThreads.Page([MailThreads.Message(2)], messageCount: 2));

        // Assert
        Assert.Equal(
            [MailMessages.Identity(1), MailMessages.Identity(2)],
            extended.Messages.Select(message => message.Email!.Id));

        Assert.False(extended.HasMore);
    }

    /// <summary>
    /// The counts come from the newer answer, because each of them describes the conversation as the deployment reads
    /// it now rather than as it read it when the first page was asked for.
    /// </summary>
    [Fact]
    public void Extended_ADeploymentThatHasSinceCountedDifferently_TakesTheNewerCount()
    {
        // Arrange
        var window = ThreadWindow.Opening(
            new ThreadOpening(MailThreads.Identity, AtMessage: null),
            MailThreads.Page([MailThreads.Message(1)], messageCount: 2, nextCursor: "after-1"));

        // Act
        var extended = window.Extended(MailThreads.Page([MailThreads.Message(2)], messageCount: 3));

        // Assert
        Assert.Equal(3, extended.MessageCount);
    }

    /// <summary>
    /// A message the answer described with no message of its own is left out rather than drawn as a line with nothing
    /// on it, and stays counted, because the count is the deployment's statement about the conversation.
    /// </summary>
    [Fact]
    public void Opening_AMessageTheAnswerDescribedWithNothing_LeavesItOutAndKeepsTheCount()
    {
        // Arrange
        var page = MailThreads.Page(
            [MailThreads.Message(1), new DeploymentThreadMessage(1, AnsweredId: null, Email: null)],
            messageCount: 2);

        // Act
        var window = ThreadWindow.Opening(new ThreadOpening(MailThreads.Identity, AtMessage: null), page);

        // Assert
        Assert.Single(window.Messages);
        Assert.Equal(2, window.MessageCount);
    }

    /// <summary>
    /// A page in flight when another conversation is opened belongs to neither, which is what holding it against the
    /// reading it was started under decides.
    /// </summary>
    [Fact]
    public void IsOf_AWindowOfAnotherConversationOrAnotherArrival_IsNotTheSameReading()
    {
        // Arrange
        var page = MailThreads.Page([MailThreads.Message(1)]);
        var window = ThreadWindow.Opening(new ThreadOpening(MailThreads.Identity, AtMessage: null), page);

        var elsewhere = ThreadWindow.Opening(new ThreadOpening(Guid.NewGuid(), AtMessage: null), page);
        var arrivedAt = ThreadWindow.Opening(
            new ThreadOpening(MailThreads.Identity, MailMessages.Identity(1)),
            page);

        // Act, Assert
        Assert.True(window.IsOf(window));
        Assert.False(window.IsOf(elsewhere));
        Assert.False(window.IsOf(arrivedAt));
    }

    /// <summary>A screen nothing has been opened in is a state of its own rather than an empty conversation.</summary>
    [Fact]
    public void Nothing_NoConversationOpened_IsNeitherOpenNorPageable()
    {
        // Act, Assert
        Assert.False(ThreadWindow.Nothing.IsOpen);
        Assert.False(ThreadWindow.Nothing.HasMore);
        Assert.Empty(ThreadWindow.Nothing.Messages);
    }
}
