// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Domain.UnitTests;

public sealed class MailIdentityTests
{
    [Fact]
    public void Create_ValidOccurrence_ProducesStableIdentity()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(42);
        var uid = ImapUid.Create(100);

        // Act
        var occurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, uid);

        // Assert
        Assert.Equal("primary", occurrence.AccountId.Value);
        Assert.Equal("INBOX", occurrence.FolderName.Value);
        Assert.Equal(42U, occurrence.UidValidity.Value);
        Assert.Equal(100U, occurrence.Uid.Value);
    }

    [Theory]
    [InlineData(0U)]
    public void Create_InvalidUid_ThrowsArgumentOutOfRangeException(uint value)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ImapUid.Create(value));
    }

    [Fact]
    public void AdvanceTo_NewerUid_ReturnsAdvancedCheckpoint()
    {
        // Arrange
        var checkpoint = SynchronizationCheckpoint.None(ImapUidValidity.Create(7));
        var uid = ImapUid.Create(9);

        // Act
        var advanced = checkpoint.AdvanceTo(uid, new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

        // Assert
        Assert.Equal(uid, advanced.LastSeenUid);
        Assert.NotNull(advanced.SynchronizedAt);
    }
}
