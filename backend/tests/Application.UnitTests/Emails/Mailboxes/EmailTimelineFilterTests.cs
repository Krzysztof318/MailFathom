// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

/// <summary>Covers what the timeline filter adds to the shared selection: the reading direction and the fingerprint.</summary>
public sealed class EmailTimelineFilterTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_DirectionThatNamesNeitherEnd_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterWith(direction: (EmailTimelineDirection)7));
    }

    [Fact]
    public void Create_NoScope_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmailTimelineFilter.Create(
            null!,
            senderAddress: null,
            recipientAddress: null,
            subjectFragment: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            isRemotelySeen: null,
            isRemotelyFlagged: null,
            keyword: null,
            hasAttachments: null,
            EmailTimelineDirection.NewestFirst));
    }

    /// <summary>The filters a timeline applies are the shared ones, validated once rather than restated here.</summary>
    [Fact]
    public void Create_StructuralFilters_ReachTheSharedSelection()
    {
        // Act
        var filter = FilterWith(senderAddress: "Anna@Example.test", subjectFragment: "  invoice  ");

        // Assert
        Assert.Equal("ANNA@EXAMPLE.TEST", filter.Selection.SenderNormalizedAddress);
        Assert.Equal("invoice", filter.Selection.SubjectFragment);
    }

    [Fact]
    public void ReadIn_NoSelection_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            EmailTimelineFilter.ReadIn(null!, EmailTimelineDirection.NewestFirst));
    }

    /// <summary>Two requests selecting the same emails in the same order continue one walk, however each was written.</summary>
    [Fact]
    public void Fingerprint_RequestsThatSelectTheSameEmails_Match()
    {
        // Arrange
        var accountsInOneOrder = MailboxScope.Create(
            SyntheticMailOwner.Deployment,
            [MailAccountId.Create("primary"), MailAccountId.Create("secondary")],
            selectedFolders: null);
        var accountsInTheOther = MailboxScope.Create(
            SyntheticMailOwner.Deployment,
            [MailAccountId.Create("secondary"), MailAccountId.Create("primary"), MailAccountId.Create("primary")],
            selectedFolders: null);

        // Act
        var first = FilterWith(scope: accountsInOneOrder, subjectFragment: "Invoice");
        var second = FilterWith(scope: accountsInTheOther, subjectFragment: "invoice");

        // Assert
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    /// <summary>A folder alias may contain the character a list is joined with, so the joined text alone cannot identify one.</summary>
    [Fact]
    public void Fingerprint_ScopesWhoseNamesShareTheirSeparators_DoNotMatch()
    {
        // Arrange
        var oneAliasCarryingTheSeparator = MailboxScope.Create(
            SyntheticMailOwner.Deployment,
            accountIds: null,
            [Folder("ARCHIVE,SENT"), Folder("TRASH")]);
        var theSameNamesSplitDifferently = MailboxScope.Create(
            SyntheticMailOwner.Deployment,
            accountIds: null,
            [Folder("ARCHIVE"), Folder("SENT,TRASH")]);

        // Act
        var first = FilterWith(scope: oneAliasCarryingTheSeparator);
        var second = FilterWith(scope: theSameNamesSplitDifferently);

        // Assert
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Fingerprint_RequestsDifferingInTheReadingDirection_DoNotMatch()
    {
        // Act
        var newestFirst = FilterWith(direction: EmailTimelineDirection.NewestFirst);
        var oldestFirst = FilterWith(direction: EmailTimelineDirection.OldestFirst);

        // Assert
        Assert.NotEqual(newestFirst.Fingerprint, oldestFirst.Fingerprint);
    }

    /// <summary>Every filter takes part, so no pair of different requests can share a cursor.</summary>
    [Fact]
    public void Fingerprint_RequestsDifferingInAnyOneFilter_DoNotMatch()
    {
        // Arrange
        var unfiltered = FilterWith();
        var variants = new[]
        {
            FilterWith(scope: MailboxScope.Create(
                SyntheticMailOwner.Deployment,
                [MailAccountId.Create("primary")],
                selectedFolders: null)),
            FilterWith(scope: MailboxScope.Create(SyntheticMailOwner.Deployment, null, [Folder("ARCHIVE")])),
            FilterWith(senderAddress: "anna@example.test"),
            FilterWith(recipientAddress: "anna@example.test"),
            FilterWith(subjectFragment: "invoice"),
            FilterWith(receivedOnOrAfter: FirstJuly),
            FilterWith(receivedBefore: FirstJuly),
            FilterWith(isRemotelySeen: true),
            FilterWith(isRemotelySeen: false),
            FilterWith(isRemotelyFlagged: true),
            FilterWith(isRemotelyFlagged: false),
            FilterWith(keyword: "$Junk"),
            FilterWith(hasAttachments: true),
            FilterWith(hasAttachments: false),
        };

        // Act
        var fingerprints = variants.Select(variant => variant.Fingerprint).Append(unfiltered.Fingerprint).ToArray();

        // Assert
        Assert.Equal(fingerprints.Length, fingerprints.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Names one folder of the account these fingerprints are all written about.</summary>
    private static MailFolderIdentity Folder(string alias) =>
        new(MailAccountId.Create("primary"), MailFolderAlias.Create(alias));

    private static EmailTimelineFilter FilterWith(
        MailboxScope? scope = null,
        string? senderAddress = null,
        string? recipientAddress = null,
        string? subjectFragment = null,
        DateTimeOffset? receivedOnOrAfter = null,
        DateTimeOffset? receivedBefore = null,
        bool? isRemotelySeen = null,
        bool? isRemotelyFlagged = null,
        string? keyword = null,
        bool? hasAttachments = null,
        EmailTimelineDirection direction = EmailTimelineDirection.NewestFirst) => EmailTimelineFilter.Create(
        scope ?? MailboxScope.NothingReadable,
        senderAddress,
        recipientAddress,
        subjectFragment,
        receivedOnOrAfter,
        receivedBefore,
        isRemotelySeen,
        isRemotelyFlagged,
        keyword,
        hasAttachments,
        direction);
}
