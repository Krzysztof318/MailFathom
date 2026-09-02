// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails.Threads;

/// <summary>Covers the four values a row hands assembly, which both write paths read through this one reader.</summary>
public sealed class ThreadedEmailsTests
{
    [Fact]
    public void Of_RowNamingItsOwnIdentifierItsParentAndItsAncestors_CarriesEveryOneOfThem()
    {
        // Arrange
        var storedEmailId = Guid.CreateVersion7();
        var parentStoredEmailId = Guid.CreateVersion7();
        var entity = EntityOf(storedEmailId);

        entity.InternetMessageId = "reply@example.test";
        entity.InReplyTo = "opening@example.test";
        entity.ThreadReferences = ["root@example.test", "opening@example.test"];
        entity.ParentStoredEmailId = parentStoredEmailId;

        // Act
        var threaded = ThreadedEmails.Of(entity);

        // Assert
        Assert.Equal(StoredEmailId.Create(storedEmailId), threaded.StoredEmailId);
        Assert.Equal("reply@example.test", threaded.InternetMessageId);
        Assert.Equal("opening@example.test", threaded.AnsweredInternetMessageId);
        Assert.Equal(["root@example.test", "opening@example.test"], threaded.ReferencedInternetMessageIds);
        Assert.Equal(StoredEmailId.Create(parentStoredEmailId), threaded.AnsweredStoredEmailId);
    }

    /// <summary>A message nothing has placed yet answers no stored message, which is what makes a first pass different from a second.</summary>
    [Fact]
    public void Of_RowNothingHasPlacedYet_AnswersNoStoredMessage()
    {
        // Arrange
        var entity = EntityOf(Guid.CreateVersion7());

        // Act
        var threaded = ThreadedEmails.Of(entity);

        // Assert
        Assert.Null(threaded.AnsweredStoredEmailId);
        Assert.Null(threaded.InternetMessageId);
        Assert.Null(threaded.AnsweredInternetMessageId);
        Assert.Empty(threaded.ReferencedInternetMessageIds);
    }

    private static StoredEmailEntity EntityOf(Guid storedEmailId) => new()
    {
        Id = storedEmailId,
        OwnerId = SyntheticMailOwner.Deployment.Value,
        MailboxAccountId = "primary",
        MailFolder = new MailFolderEntity
        {
            OwnerId = SyntheticMailOwner.Deployment.Value,
            MailboxAccountId = "primary",
            Alias = "inbox",
            RemotePath = "INBOX",
            MailboxAccount = new MailboxAccountEntity { OwnerId = SyntheticMailOwner.Deployment.Value, Id = "primary" },
        },
    };
}
