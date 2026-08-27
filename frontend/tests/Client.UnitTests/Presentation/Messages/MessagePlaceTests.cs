// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>Where a list is drawn from, read out of the scope without what is selected inside it.</summary>
public sealed class MessagePlaceTests
{
    /// <summary>
    /// What is selected is not part of the place, which is the whole reason the type exists: a list keyed on the whole
    /// scope would read the folder again every time somebody clicked a row in it.
    /// </summary>
    [Fact]
    public void Of_TwoScopesDifferingOnlyInWhatIsSelected_NameOnePlace()
    {
        // Arrange
        var scope = new WorkspaceScope { Account = "work", Folder = "INBOX" };
        var withSomethingSelected = scope with { Selection = ImmutableArray.Create("117", "118") };

        // Act, Assert
        Assert.Equal(MessagePlace.Of(scope), MessagePlace.Of(withSomethingSelected));
    }

    /// <summary>The three names a scope narrows by are the three a place carries.</summary>
    [Fact]
    public void Of_AScopeNarrowingSomewhere_CarriesTheAccountTheFolderAndTheRole()
    {
        // Act
        var place = MessagePlace.Of(new WorkspaceScope { Account = "work", Folder = "INBOX", Role = "Inbox" });

        // Assert
        Assert.Equal(new MessagePlace("work", "INBOX", "Inbox"), place);
    }

    /// <summary>A scope nothing has answered yet is every mailbox rather than a place describing nowhere.</summary>
    [Fact]
    public void Of_NoScopeAtAll_IsEverything()
    {
        // Act, Assert
        Assert.Equal(MessagePlace.Everything, MessagePlace.Of(null));
        Assert.Equal(MessagePlace.Everything, MessagePlace.Of(WorkspaceScope.Everything));
    }

    /// <summary>Two places are told apart by what they are remembered as, whichever of the three names differs.</summary>
    [Fact]
    public void RememberedAs_PlacesDifferingInAnyOfTheirNames_AreRememberedApart()
    {
        // Arrange
        var keys = new[]
        {
            MessagePlace.Everything,
            new MessagePlace("work", Folder: null, Role: null),
            new MessagePlace("work", "INBOX", Role: null),
            new MessagePlace(Account: null, Folder: null, "Inbox"),
        }.Select(place => place.RememberedAs);

        // Act, Assert
        Assert.Equal(4, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A folder or a role somebody chose is a place they went to, so its junk takes part without being asked for —
    /// which is what keeps a junk folder from being drawn as an empty one.
    /// </summary>
    [Fact]
    public void IsChosenFolder_AFolderOrARole_IsOneAndAnAccountIsNot()
    {
        // Act, Assert
        Assert.True(new MessagePlace("work", "JUNK", Role: null).IsChosenFolder);
        Assert.True(new MessagePlace(Account: null, Folder: null, "Junk").IsChosenFolder);
        Assert.False(new MessagePlace("work", Folder: null, Role: null).IsChosenFolder);
        Assert.False(MessagePlace.Everything.IsChosenFolder);
    }

    /// <summary>
    /// Mail somebody sent is drawn by who it went to, because every row of it came from the same person. It is
    /// answered from the role, so a folder chosen by its alias — which reaches this client without one — keeps drawing
    /// senders.
    /// </summary>
    [Theory]
    [InlineData("Sent", true)]
    [InlineData("Drafts", true)]
    [InlineData("Outbox", true)]
    [InlineData("Inbox", false)]
    [InlineData("Archive", false)]
    [InlineData(null, false)]
    public void ShowsRecipients_ARoleMailIsSentUnder_DrawsRecipientsRatherThanSenders(string? role, bool expected)
    {
        // Act, Assert
        Assert.Equal(expected, new MessagePlace(Account: null, Folder: null, role).ShowsRecipients);
    }
}
