// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Filing;

public sealed class OutgoingMailFilingRecordTests
{
    private static readonly OutgoingEmailId Record =
        OutgoingEmailId.Create(Guid.Parse("0198f0a0-2222-7000-8000-000000000001"));

    private static readonly RemoteFolderPath SentFolder = RemoteFolderPath.Create("INBOX.Sent");

    private static readonly ImapUidValidity UidValidity = ImapUidValidity.Create(42);

    /// <summary>The server named the occurrence, so the join is its own statement rather than a comparison.</summary>
    [Fact]
    public void AccountsForPlacementAt_TheOccurrenceTheServerNamed_IsTheCopyThisFilingAppended()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.Reported(UidValidity, ImapUid.Create(7)));

        // Act
        var accountsFor = filing.AccountsForPlacementAt(SentFolder, UidValidity, ImapUid.Create(7));

        // Assert
        Assert.True(accountsFor);
    }

    /// <summary>A folder recreated between the append and the discovery renumbered every message in it.</summary>
    [Fact]
    public void AccountsForPlacementAt_TheSameUidInANewUidSpace_IsAnotherMessage()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.Reported(UidValidity, ImapUid.Create(7)));

        // Act
        var accountsFor = filing.AccountsForPlacementAt(SentFolder, ImapUidValidity.Create(43), ImapUid.Create(7));

        // Assert
        Assert.False(accountsFor);
    }

    /// <summary>The same message filed into two folders belongs to the filing whose folder it was found in.</summary>
    [Fact]
    public void AccountsForPlacementAt_AnotherFolder_IsNotThisFilingsCopy()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.Reported(UidValidity, ImapUid.Create(7)));

        // Act
        var accountsFor = filing.AccountsForPlacementAt(
            RemoteFolderPath.Create("INBOX.Drafts"),
            UidValidity,
            ImapUid.Create(7));

        // Assert
        Assert.False(accountsFor);
    }

    /// <summary>A server advertising no UIDPLUS named nothing, so the identity in the appended bytes is the join.</summary>
    [Fact]
    public void AccountsForMessageAt_TheIdentityTheAppendedBytesCarry_IsTheCopyThisFilingAppended()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.NotReported(), messageId: "mint-1@mailfathom.invalid");

        // Act
        var accountsFor = filing.AccountsForMessageAt(SentFolder, "mint-1@mailfathom.invalid");

        // Assert
        Assert.True(accountsFor);
    }

    /// <summary>A discovery the server reported no identity for is joined to nothing rather than to whatever sorted first.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("someone-elses@example.test")]
    public void AccountsForMessageAt_AnotherIdentity_IsNotThisFilingsCopy(string? discoveredMessageId)
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.NotReported(), messageId: "mint-1@mailfathom.invalid");

        // Act
        var accountsFor = filing.AccountsForMessageAt(SentFolder, discoveredMessageId);

        // Assert
        Assert.False(accountsFor);
    }

    /// <summary>
    /// A row that has already answered for a discovery answers for no second one. Without it, a folder recreated under
    /// reused UIDs would attribute a stranger's message to a copy this deployment filed long ago.
    /// </summary>
    [Fact]
    public void AccountsForPlacementAt_AFilingSynchronizationAlreadyMet_IsNoLongerACandidate()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.Reported(UidValidity, ImapUid.Create(7))) with
        {
            ObservedAt = DateTimeOffset.UnixEpoch,
        };

        // Act
        var accountsFor = filing.AccountsForPlacementAt(SentFolder, UidValidity, ImapUid.Create(7));

        // Assert
        Assert.False(accountsFor);
    }

    /// <summary>An issued append names no copy anybody can point at, which is what its unknown outcome means.</summary>
    [Fact]
    public void HasUnknownOutcome_AnIssuedAppend_NamesNoCopyAndJoinsNothing()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.Reported(UidValidity, ImapUid.Create(7))) with
        {
            Stage = OutgoingMailFilingStage.Issued,
        };

        // Act & Assert
        Assert.True(filing.HasUnknownOutcome);
        Assert.True(filing.IsStanding);
        Assert.False(filing.AccountsForPlacementAt(SentFolder, UidValidity, ImapUid.Create(7)));
    }

    /// <summary>A withdrawn copy is no longer in the folder, so nothing stands there to be met.</summary>
    [Fact]
    public void IsStanding_AWithdrawnCopy_IsNoLongerInTheFolder()
    {
        // Arrange
        var filing = Confirmed(RemoteEmailPlacement.Reported(UidValidity, ImapUid.Create(7))) with
        {
            Stage = OutgoingMailFilingStage.Withdrawn,
            WithdrawnAt = DateTimeOffset.UnixEpoch,
        };

        // Act & Assert
        Assert.False(filing.IsStanding);
        Assert.False(filing.HasUnknownOutcome);
        Assert.False(filing.AccountsForPlacementAt(SentFolder, UidValidity, ImapUid.Create(7)));
    }

    private static OutgoingMailFilingRecord Confirmed(RemoteEmailPlacement placement, string? messageId = null) => new()
    {
        OutgoingEmailId = Record,
        Filing = OutgoingMailFiling.Sent,
        FolderAlias = MailFolderAlias.Create("sent"),
        FolderPath = SentFolder,
        Stage = OutgoingMailFilingStage.Confirmed,
        Placement = placement,
        InternetMessageId = messageId,
        AppendedAt = DateTimeOffset.UnixEpoch,
        ObservedAt = null,
        WithdrawnAt = null,
    };
}
