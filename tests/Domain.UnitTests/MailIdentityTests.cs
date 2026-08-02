// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using Xunit;

namespace MailFathom.Domain.UnitTests;

public sealed class MailIdentityTests
{
    [Fact]
    public void Create_ValidOccurrence_ProducesStableIdentity()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderResolutionId = new MailFolderResolutionId(
            MailFolderAlias.Create("inbox"),
            MailFolderResolutionGeneration.First);
        var uidValidity = ImapUidValidity.Create(42);
        var uid = ImapUid.Create(100);

        // Act
        var occurrence = EmailOccurrenceId.Create(accountId, folderResolutionId, uidValidity, uid);

        // Assert
        Assert.Equal("primary", occurrence.AccountId.Value);
        Assert.Equal("INBOX", occurrence.FolderResolutionId.Alias.Value);
        Assert.Equal(1, occurrence.FolderResolutionId.Generation.Value);
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
    public void Create_ValidStoredEmailId_PreservesValue()
    {
        // Arrange
        var value = Guid.CreateVersion7();

        // Act
        var storedEmailId = StoredEmailId.Create(value);

        // Assert
        Assert.Equal(value, storedEmailId.Value);
    }

    [Fact]
    public void Create_EmptyStoredEmailId_ThrowsArgumentException()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => StoredEmailId.Create(Guid.Empty));
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
