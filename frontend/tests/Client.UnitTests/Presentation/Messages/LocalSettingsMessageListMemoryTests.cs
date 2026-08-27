// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>How an arrangement is written down, and what it reads back as on the run after this one.</summary>
/// <remarks>
/// What the store itself does is not asserted here: <c>ApplicationData.LocalSettings</c> is a platform API a
/// unit-test host has none of, so what a test can reach is the pair of translations either side of it — which is also
/// where every value that could be written wrongly actually lives.
/// </remarks>
public sealed class LocalSettingsMessageListMemoryTests
{
    /// <summary>A list keeping everything writes no filters at all, which is what a first run also reads.</summary>
    [Fact]
    public void JoinedKeeps_AListKeepingEverything_WritesNothing()
    {
        // Act, Assert
        Assert.Equal(string.Empty, LocalSettingsMessageListMemory.JoinedKeeps(MessageListArrangement.Default));
    }

    /// <summary>Every filter somebody put in force is written, because each of them is one the list reopens under.</summary>
    [Fact]
    public void JoinedKeeps_AListNarrowedEveryWay_WritesEachFilter()
    {
        // Arrange
        var narrowed = new MessageListArrangement
        {
            UnreadOnly = true,
            FlaggedOnly = true,
            WithAttachmentsOnly = true,
            IncludeJunk = true,
        };

        // Act
        var written = LocalSettingsMessageListMemory.JoinedKeeps(narrowed);

        // Assert
        Assert.Equal("unread flagged attachments junk", written);
    }

    /// <summary>What was written reads back as itself, which is the only property either half is worth having.</summary>
    [Theory]
    [InlineData(MailTimelineOrder.NewestFirst, false, false, false, false)]
    [InlineData(MailTimelineOrder.OldestFirst, false, false, false, false)]
    [InlineData(MailTimelineOrder.NewestFirst, true, false, false, false)]
    [InlineData(MailTimelineOrder.OldestFirst, false, true, false, false)]
    [InlineData(MailTimelineOrder.NewestFirst, false, false, true, false)]
    [InlineData(MailTimelineOrder.OldestFirst, false, false, false, true)]
    [InlineData(MailTimelineOrder.OldestFirst, true, true, true, true)]
    public void ArrangementOf_WhatWasWritten_ReadsBackAsTheSameArrangement(
        MailTimelineOrder order,
        bool unreadOnly,
        bool flaggedOnly,
        bool withAttachmentsOnly,
        bool includeJunk)
    {
        // Arrange
        var arrangement = new MessageListArrangement
        {
            Order = order,
            UnreadOnly = unreadOnly,
            FlaggedOnly = flaggedOnly,
            WithAttachmentsOnly = withAttachmentsOnly,
            IncludeJunk = includeJunk,
        };

        // Act
        var read = LocalSettingsMessageListMemory.ArrangementOf(
            order is MailTimelineOrder.OldestFirst ? "oldestFirst" : "newestFirst",
            LocalSettingsMessageListMemory.JoinedKeeps(arrangement));

        // Assert
        Assert.Equal(arrangement, read);
    }

    /// <summary>
    /// A store that answers nothing is a first run rather than a failure to start, so it reads as the arrangement a
    /// list arrives with.
    /// </summary>
    [Fact]
    public void ArrangementOf_AStoreHoldingNothing_ReadsAsTheArrangementAListArrivesWith()
    {
        // Act, Assert
        Assert.Equal(MessageListArrangement.Default, LocalSettingsMessageListMemory.ArrangementOf(null, null));
        Assert.Equal(
            MessageListArrangement.Default,
            LocalSettingsMessageListMemory.ArrangementOf(string.Empty, string.Empty));
    }

    /// <summary>
    /// An entry nothing here ever wrote claims nothing about the list. The mapping is closed for that reason: parsing
    /// the enumeration would read a number, a member name, and a comma-separated list as answers as well.
    /// </summary>
    [Fact]
    public void ArrangementOf_AnEntryNothingHereWrote_ClaimsNothingAboutTheList()
    {
        // Act
        var numbered = LocalSettingsMessageListMemory.ArrangementOf("1", "1");
        var named = LocalSettingsMessageListMemory.ArrangementOf("OldestFirst", "UnreadOnly");

        // Assert
        Assert.Equal(MessageListArrangement.Default, numbered);
        Assert.Equal(MessageListArrangement.Default, named);
    }

    /// <summary>A place nobody has read yet opens at the leading end of the list, arranged as a list arrives.</summary>
    [Fact]
    public void Nothing_APlaceNobodyHasReadYet_OpensAtTheLeadingEnd()
    {
        // Act
        var nothing = RememberedMessageList.Nothing("workINBOX");

        // Assert
        Assert.Equal("workINBOX", nothing.PlaceKey);
        Assert.Null(nothing.Cursor);
        Assert.Equal(MailTimelinePageDirection.Forward, nothing.Direction);
        Assert.Equal(MessageListArrangement.Default, nothing.Arrangement);
    }

    /// <summary>Writing the filters of no arrangement at all would be writing a value composed from nothing.</summary>
    [Fact]
    public void JoinedKeeps_NoArrangement_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => LocalSettingsMessageListMemory.JoinedKeeps(null!));
    }
}
