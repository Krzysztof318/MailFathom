// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers what the timeline filter validates, what it normalizes, and which requests share a fingerprint.</summary>
public sealed class EmailTimelineFilterTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AddressFilters_KeepTheComparisonFormPersistenceIndexes()
    {
        // Act
        var filter = FilterWith(senderAddress: "Anna@Example.test", recipientAddress: " bob@example.TEST ");

        // Assert
        Assert.Equal("ANNA@EXAMPLE.TEST", filter.SenderNormalizedAddress);
        Assert.Equal("BOB@EXAMPLE.TEST", filter.RecipientNormalizedAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankAddressFilter_NamesNoSender(string? senderAddress)
    {
        // Act
        var filter = FilterWith(senderAddress: senderAddress);

        // Assert
        Assert.Null(filter.SenderNormalizedAddress);
    }

    /// <summary>An address that matches no stored participant is refused, so a caller is not told the mailbox is empty.</summary>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("@example.test")]
    [InlineData("anna@")]
    public void Create_UnusableSenderAddress_IsRejected(string senderAddress)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() => FilterWith(senderAddress: senderAddress));

        // Assert
        Assert.Equal("sender address", failure.FilterName);
    }

    /// <summary>Nothing in the address grammar bounds a length, so a filter longer than any column could hold is refused.</summary>
    [Fact]
    public void Create_AddressFilterLongerThanTheLimit_IsRejected()
    {
        // Arrange
        var localPart = new string('a', EmailTimelineFilter.MaximumAddressFilterLength);
        var overlyLongAddress = $"{localPart}@example.test";

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            FilterWith(recipientAddress: overlyLongAddress));

        // Assert
        Assert.Equal("recipient address", failure.FilterName);
    }

    [Fact]
    public void Create_SubjectFragmentLongerThanTheLimit_IsRejected()
    {
        // Arrange
        var overlyLongFragment = new string('a', EmailTimelineFilter.MaximumSubjectFragmentLength + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            FilterWith(subjectFragment: overlyLongFragment));

        // Assert
        Assert.Equal("subject fragment", failure.FilterName);
    }

    /// <summary>PostgreSQL text holds no zero byte, so a control character is refused rather than sent to a parameter.</summary>
    [Theory]
    [InlineData((char)0x00)]
    [InlineData((char)0x07)]
    [InlineData((char)0x1f)]
    public void Create_SubjectFragmentCarryingAControlCharacter_IsRejected(char controlCharacter)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            FilterWith(subjectFragment: $"quarterly{controlCharacter}report"));

        // Assert
        Assert.Equal("subject fragment", failure.FilterName);
    }

    /// <summary>Trimming already removes the whitespace controls, and what it leaves is not a fragment of any subject.</summary>
    [Fact]
    public void Create_SubjectFragmentWrappedInWhitespaceControls_IsAccepted()
    {
        // Act
        var filter = FilterWith(subjectFragment: "\tinvoice\r\n");

        // Assert
        Assert.Equal("invoice", filter.SubjectFragment);
    }

    [Fact]
    public void Create_SubjectFragment_IsTrimmedRatherThanTakenLiterally()
    {
        // Act
        var filter = FilterWith(subjectFragment: "  invoice  ");

        // Assert
        Assert.Equal("invoice", filter.SubjectFragment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ReceivedRangeEndingWhereOrBeforeItStarts_IsRejected(int endOffsetDays)
    {
        // Act, Assert
        Assert.Throws<MailboxQueryFilterInvalidException>(() => FilterWith(
            receivedOnOrAfter: FirstJuly,
            receivedBefore: FirstJuly.AddDays(endOffsetDays)));
    }

    /// <summary>An unbounded range is allowed; only an unbounded page is not.</summary>
    [Fact]
    public void Create_ReceivedRangeWithOneOpenEnd_IsAccepted()
    {
        // Act
        var openEnded = FilterWith(receivedOnOrAfter: FirstJuly);
        var openStarted = FilterWith(receivedBefore: FirstJuly);

        // Assert
        Assert.Null(openEnded.ReceivedBefore);
        Assert.Null(openStarted.ReceivedOnOrAfter);
    }

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
            hasAttachments: null,
            EmailTimelineDirection.NewestFirst));
    }

    /// <summary>Two requests selecting the same emails in the same order continue one walk, however each was written.</summary>
    [Fact]
    public void Fingerprint_RequestsThatSelectTheSameEmails_Match()
    {
        // Arrange
        var accountsInOneOrder = MailboxScope.Create(
            [MailAccountId.Create("primary"), MailAccountId.Create("secondary")],
            folderAliases: null);
        var accountsInTheOther = MailboxScope.Create(
            [MailAccountId.Create("secondary"), MailAccountId.Create("primary"), MailAccountId.Create("primary")],
            folderAliases: null);

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
            accountIds: null,
            [MailFolderAlias.Create("ARCHIVE,SENT"), MailFolderAlias.Create("TRASH")]);
        var theSameNamesSplitDifferently = MailboxScope.Create(
            accountIds: null,
            [MailFolderAlias.Create("ARCHIVE"), MailFolderAlias.Create("SENT,TRASH")]);

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
            FilterWith(scope: MailboxScope.Create([MailAccountId.Create("primary")], folderAliases: null)),
            FilterWith(scope: MailboxScope.Create(null, [MailFolderAlias.Create("ARCHIVE")])),
            FilterWith(senderAddress: "anna@example.test"),
            FilterWith(recipientAddress: "anna@example.test"),
            FilterWith(subjectFragment: "invoice"),
            FilterWith(receivedOnOrAfter: FirstJuly),
            FilterWith(receivedBefore: FirstJuly),
            FilterWith(isRemotelySeen: true),
            FilterWith(isRemotelySeen: false),
            FilterWith(hasAttachments: true),
            FilterWith(hasAttachments: false),
        };

        // Act
        var fingerprints = variants.Select(variant => variant.Fingerprint).Append(unfiltered.Fingerprint).ToArray();

        // Assert
        Assert.Equal(fingerprints.Length, fingerprints.Distinct(StringComparer.Ordinal).Count());
    }

    private static EmailTimelineFilter FilterWith(
        MailboxScope? scope = null,
        string? senderAddress = null,
        string? recipientAddress = null,
        string? subjectFragment = null,
        DateTimeOffset? receivedOnOrAfter = null,
        DateTimeOffset? receivedBefore = null,
        bool? isRemotelySeen = null,
        bool? hasAttachments = null,
        EmailTimelineDirection direction = EmailTimelineDirection.NewestFirst) => EmailTimelineFilter.Create(
        scope ?? MailboxScope.Unrestricted,
        senderAddress,
        recipientAddress,
        subjectFragment,
        receivedOnOrAfter,
        receivedBefore,
        isRemotelySeen,
        hasAttachments,
        direction);
}
