// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.UnitTests.Backend.Timeline;

/// <summary>What one page of the list is asked for as, which is the whole of what this client says about a request.</summary>
public sealed class MailTimelineQueryTests
{
    /// <summary>A query stating nothing narrows nothing, which is the unified list read from its leading end.</summary>
    [Fact]
    public void QueryString_AQueryNarrowingNothing_StatesOnlyTheOrderAndTheDirection()
    {
        // Act
        var written = new MailTimelineQuery().QueryString();

        // Assert
        Assert.Equal("?order=newestFirst&direction=forward", written);
    }

    /// <summary>
    /// A filter keeping everything is left out rather than sent as "both", so a request says what somebody narrowed
    /// and nothing else.
    /// </summary>
    [Fact]
    public void QueryString_AFilterKeepingEverything_IsLeftUnstated()
    {
        // Arrange
        var query = new MailTimelineQuery { Unread = null, Flagged = false, IncludeJunk = false };

        // Act
        var written = query.QueryString();

        // Assert
        Assert.DoesNotContain("unread", written, StringComparison.Ordinal);
        Assert.DoesNotContain("includeJunk", written, StringComparison.Ordinal);
        Assert.Contains("flagged=false", written, StringComparison.Ordinal);
    }

    /// <summary>Every value the request carries reaches the wire under the name the surface publishes.</summary>
    [Fact]
    public void QueryString_AQueryStatingEverything_WritesEachValueUnderItsPublishedName()
    {
        // Arrange
        var query = new MailTimelineQuery
        {
            Account = "work",
            Folder = "INBOX",
            IncludeJunk = true,
            Unread = true,
            Flagged = true,
            HasAttachments = true,
            Order = MailTimelineOrder.OldestFirst,
            Direction = MailTimelinePageDirection.Backward,
            PageSize = 50,
            Cursor = "the-cursor",
        };

        // Act
        var written = query.QueryString();

        // Assert
        Assert.Equal(
            "?account=work&folder=INBOX&includeJunk=true&unread=true&flagged=true&hasAttachments=true"
            + "&order=oldestFirst&direction=backward&pageSize=50&cursor=the-cursor",
            written);
    }

    /// <summary>
    /// A folder alias is a name a mail server chose and a cursor is a value this client received, so neither may be
    /// written raw into a URL whatever either happens to look like today.
    /// </summary>
    [Fact]
    public void QueryString_ValuesThisClientDidNotCompose_AreEscapedForTheQueryString()
    {
        // Arrange
        var query = new MailTimelineQuery { Folder = "Projects & plans/2024", Cursor = "a+b=c&d" };

        // Act
        var written = query.QueryString();

        // Assert
        Assert.Contains("folder=Projects%20%26%20plans%2F2024", written, StringComparison.Ordinal);
        Assert.Contains("cursor=a%2Bb%3Dc%26d", written, StringComparison.Ordinal);
    }

    /// <summary>A role taken across mailboxes is written as the folder, because that is what it is on this surface.</summary>
    [Fact]
    public void QueryString_ARoleWrittenAsAFolder_KeepsTheSchemeTheSurfaceReads()
    {
        // Act
        var written = new MailTimelineQuery { Folder = "role:Inbox" }.QueryString();

        // Assert
        Assert.Contains("folder=role%3AInbox", written, StringComparison.Ordinal);
    }

    /// <summary>A page size is written the way a machine reads a number rather than the way a reader's culture does.</summary>
    [Fact]
    public void QueryString_APageSize_IsWrittenInvariantly()
    {
        // Act
        var written = new MailTimelineQuery { PageSize = 1000 }.QueryString();

        // Assert
        Assert.Contains("pageSize=1000", written, StringComparison.Ordinal);
    }
}
