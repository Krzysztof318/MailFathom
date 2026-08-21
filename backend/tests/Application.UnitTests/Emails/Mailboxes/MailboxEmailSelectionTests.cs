// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

/// <summary>Covers what the structural filters every mailbox read shares validate and normalize.</summary>
public sealed class MailboxEmailSelectionTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    [Fact]
    public void Create_AddressFilters_KeepTheComparisonFormPersistenceIndexes()
    {
        // Act
        var selection = SelectionWith(senderAddress: "Anna@Example.test", recipientAddress: " bob@example.TEST ");

        // Assert
        Assert.Equal("ANNA@EXAMPLE.TEST", selection.SenderNormalizedAddress);
        Assert.Equal("BOB@EXAMPLE.TEST", selection.RecipientNormalizedAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankAddressFilter_NamesNoSender(string? senderAddress)
    {
        // Act
        var selection = SelectionWith(senderAddress: senderAddress);

        // Assert
        Assert.Null(selection.SenderNormalizedAddress);
    }

    /// <summary>An address that matches no stored participant is refused, so a caller is not told the mailbox is empty.</summary>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("@example.test")]
    [InlineData("anna@")]
    public void Create_UnusableSenderAddress_IsRejected(string senderAddress)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            SelectionWith(senderAddress: senderAddress));

        // Assert
        Assert.Equal("sender address", failure.FilterName);
    }

    /// <summary>A keyword is compared without regard to case, so which case a caller wrote it in never decides a match.</summary>
    [Theory]
    [InlineData("$Junk")]
    [InlineData("$JUNK")]
    [InlineData("  $junk  ")]
    public void Create_KeywordFilter_KeepsTheComparisonFormStoredKeywordsUse(string keyword)
    {
        // Act
        var selection = SelectionWith(keyword: keyword);

        // Assert
        Assert.Equal("$JUNK", selection.Keyword);
    }

    /// <summary>An empty keyword is no filter rather than a filter for nothing, which is how every other optional text filter reads.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankKeywordFilter_NamesNoKeyword(string? keyword)
    {
        // Act
        var selection = SelectionWith(keyword: keyword);

        // Assert
        Assert.Null(selection.Keyword);
    }

    /// <summary>No stored keyword carries a control character, so such a filter would match nothing and is refused instead of returning an empty page that reads as an answer.</summary>
    [Fact]
    public void Create_KeywordCarryingAControlCharacter_IsRejected()
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            SelectionWith(keyword: "$Ju\u0001nk"));

        // Assert
        Assert.Equal("keyword", failure.FilterName);

        // The refusal says why, because a keyword is free text and a caller reading that their value names no known
        // identity would go looking for a vocabulary this filter does not have.
        Assert.Equal(
            MailboxQueryFilterInvalidException.ContainsControlCharacter("keyword").Message,
            failure.Message);
    }

    /// <summary>The bound is the one the stored keywords were kept under, so a longer filter could not match any of them.</summary>
    [Fact]
    public void Create_KeywordLongerThanTheLimit_IsRejected()
    {
        // Arrange
        var overlyLongKeyword = new string('a', RemoteEmailKeywords.MaximumKeywordLength + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            SelectionWith(keyword: overlyLongKeyword));

        // Assert
        Assert.Equal("keyword", failure.FilterName);
    }

    /// <summary>
    /// A cursor names a boundary in one walk over one filtered set, so every filter is part of the text it is
    /// authenticated against. A flag or keyword filter left out of it would let a cursor issued over the starred mail
    /// be presented against the whole mailbox.
    /// </summary>
    [Fact]
    public void Create_TheFlagAndKeywordFilters_ArePartOfWhatACursorIsAuthenticatedAgainst()
    {
        // Act
        var unfiltered = SelectionWith();
        var flagged = SelectionWith(isRemotelyFlagged: true);
        var unflagged = SelectionWith(isRemotelyFlagged: false);
        var keyworded = SelectionWith(keyword: "$Junk");

        // Assert
        Assert.Equal(4, new[] { unfiltered, flagged, unflagged, keyworded }.Select(selection => selection.CanonicalText).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Two requests that wrote one keyword in different cases select the same mail, so they are one walk and their cursors are interchangeable.</summary>
    [Fact]
    public void Create_OneKeywordWrittenInTwoCases_IsOneWalk()
    {
        // Act
        var written = SelectionWith(keyword: "$Junk");
        var shouted = SelectionWith(keyword: "$JUNK");

        // Assert
        Assert.Equal(written.CanonicalText, shouted.CanonicalText);
    }

    /// <summary>
    /// A free-text filter can be written as whatever marks absence, so the marker cannot be the whole of how absence is
    /// said. Were it, a walk over the mail labelled <c>-</c> and a walk over the whole mailbox would share one
    /// fingerprint, and a cursor issued in the middle of the first would be accepted against the second.
    /// </summary>
    [Theory]
    [InlineData("-")]
    [InlineData("0")]
    [InlineData("1")]
    public void Create_AFilterWrittenAsTheMarkerForAbsence_IsStillItsOwnWalk(string filter)
    {
        // Act
        var unfiltered = SelectionWith();
        var keyworded = SelectionWith(keyword: filter);
        var subjectFiltered = SelectionWith(subjectFragment: filter);

        // Assert
        Assert.Equal(
            3,
            new[] { unfiltered, keyworded, subjectFiltered }
                .Select(selection => selection.CanonicalText)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    /// <summary>Nothing in the address grammar bounds a length, so a filter longer than any column could hold is refused.</summary>
    [Fact]
    public void Create_AddressFilterLongerThanTheLimit_IsRejected()
    {
        // Arrange
        var localPart = new string('a', MailboxEmailSelection.MaximumAddressFilterLength);
        var overlyLongAddress = $"{localPart}@example.test";

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            SelectionWith(recipientAddress: overlyLongAddress));

        // Assert
        Assert.Equal("recipient address", failure.FilterName);
    }

    [Fact]
    public void Create_SubjectFragmentLongerThanTheLimit_IsRejected()
    {
        // Arrange
        var overlyLongFragment = new string('a', MailboxEmailSelection.MaximumSubjectFragmentLength + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            SelectionWith(subjectFragment: overlyLongFragment));

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
            SelectionWith(subjectFragment: $"quarterly{controlCharacter}report"));

        // Assert
        Assert.Equal("subject fragment", failure.FilterName);
    }

    /// <summary>Trimming already removes the whitespace controls, and what it leaves is not a fragment of any subject.</summary>
    [Fact]
    public void Create_SubjectFragmentWrappedInWhitespaceControls_IsAccepted()
    {
        // Act
        var selection = SelectionWith(subjectFragment: "\tinvoice\r\n");

        // Assert
        Assert.Equal("invoice", selection.SubjectFragment);
    }

    [Fact]
    public void Create_SubjectFragment_IsTrimmedRatherThanTakenLiterally()
    {
        // Act
        var selection = SelectionWith(subjectFragment: "  invoice  ");

        // Assert
        Assert.Equal("invoice", selection.SubjectFragment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ReceivedRangeEndingWhereOrBeforeItStarts_IsRejected(int endOffsetDays)
    {
        // Act, Assert
        Assert.Throws<MailboxQueryFilterInvalidException>(() => SelectionWith(
            receivedOnOrAfter: FirstJuly,
            receivedBefore: FirstJuly.AddDays(endOffsetDays)));
    }

    /// <summary>An unbounded range is allowed; only an unbounded result is not.</summary>
    [Fact]
    public void Create_ReceivedRangeWithOneOpenEnd_IsAccepted()
    {
        // Act
        var openEnded = SelectionWith(receivedOnOrAfter: FirstJuly);
        var openStarted = SelectionWith(receivedBefore: FirstJuly);

        // Assert
        Assert.Null(openEnded.ReceivedBefore);
        Assert.Null(openStarted.ReceivedOnOrAfter);
    }

    /// <summary>A bound is an instant, so the offset it was written in reaches neither the query nor the cursor.</summary>
    /// <remarks>
    /// The offset matters beyond tidiness: a <c>timestamptz</c> parameter accepts offset zero and nothing else, so a
    /// bound left at the offset a caller wrote fails the listing instead of selecting from it.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(-8)]
    public void Create_ReceivedRangeWrittenAtANonZeroOffset_IsHeldAsTheSameInstantInUtc(int offsetHours)
    {
        // Arrange
        var offset = TimeSpan.FromHours(offsetHours);

        // Act
        var written = SelectionWith(
            receivedOnOrAfter: FirstJuly.ToOffset(offset),
            receivedBefore: FirstJuly.AddDays(1).ToOffset(offset));
        var inUtc = SelectionWith(receivedOnOrAfter: FirstJuly, receivedBefore: FirstJuly.AddDays(1));

        // Assert
        Assert.Equal(TimeSpan.Zero, written.ReceivedOnOrAfter?.Offset);
        Assert.Equal(TimeSpan.Zero, written.ReceivedBefore?.Offset);
        Assert.Equal(inUtc.ReceivedOnOrAfter, written.ReceivedOnOrAfter);
        Assert.Equal(inUtc.ReceivedBefore, written.ReceivedBefore);

        // The canonical text is what a cursor is authenticated against, so the two requests have to be one walk.
        Assert.Equal(inUtc.CanonicalText, written.CanonicalText);
    }

    /// <summary>The empty-range refusal compares instants, so it survives two bounds written at different offsets.</summary>
    [Fact]
    public void Create_ReceivedRangeEndingBeforeItStartsAcrossOffsets_IsStillRejected()
    {
        // Act, Assert
        Assert.Throws<MailboxQueryFilterInvalidException>(() => SelectionWith(
            receivedOnOrAfter: FirstJuly.ToOffset(TimeSpan.FromHours(2)),
            receivedBefore: FirstJuly.AddHours(-1).ToOffset(TimeSpan.FromHours(-8))));
    }

    /// <summary>Including junk adds rows in the middle of an ordering, so a walk cannot be resumed under the other answer.</summary>
    [Fact]
    public void Create_TheCallersAnswerAboutJunk_IsPartOfWhatACursorIsAuthenticatedAgainst()
    {
        // Arrange
        var resolver = ResolverWithJunkFolder();

        // Act
        var excludingJunk = SelectionWith(resolver.ReadableScope([], [], JunkMailInclusion.Excluded));
        var includingJunk = SelectionWith(resolver.ReadableScope([], [], JunkMailInclusion.Included));

        // Assert
        Assert.NotEqual(excludingJunk.CanonicalText, includingJunk.CanonicalText);
    }

    /// <summary>A configured folder is not a filter the caller chose, so mapping one must not invalidate an outstanding cursor.</summary>
    [Fact]
    public void Create_TheFoldersConfigurationWithholds_StayOutOfTheCursorFingerprint()
    {
        // Arrange
        var beforeTheMapping = ResolverWithJunkFolder(StubJunkMailFolderCatalog.None);
        var afterTheMapping = ResolverWithJunkFolder();

        // Act
        var before = SelectionWith(beforeTheMapping.ReadableScope([], [], JunkMailInclusion.Excluded));
        var after = SelectionWith(afterTheMapping.ReadableScope([], [], JunkMailInclusion.Excluded));

        // Assert
        Assert.Equal(before.CanonicalText, after.CanonicalText);
    }

    [Fact]
    public void Create_NoScope_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailboxEmailSelection.Create(
            null!,
            senderAddress: null,
            recipientAddress: null,
            subjectFragment: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            isRemotelySeen: null,
            isRemotelyFlagged: null,
            keyword: null,
            hasAttachments: null));
    }

    /// <summary>Builds the resolver a mailbox read gets its scope from, since the scope's own narrowing is not public.</summary>
    private static MailboxScopeResolver ResolverWithJunkFolder(IJunkMailFolderCatalog? junkFolders = null)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            new ServedMailAccount(
                Account,
                MailAccountDisplayName.Create("Work mail"),
                MailSynchronizationMode.Polling),
        ]);

        return new MailboxScopeResolver(
            catalog,
            StubMailFolderParticipation.Nothing,
            junkFolders ?? StubJunkMailFolderCatalog.Naming(
                new MailFolderIdentity(Account, MailFolderAlias.Create("JUNK"))),
            StubMailFolderMappings.ResolvingNothing);
    }

    private static MailboxEmailSelection SelectionWith(
        MailboxScope? scope = null,
        string? senderAddress = null,
        string? recipientAddress = null,
        string? subjectFragment = null,
        DateTimeOffset? receivedOnOrAfter = null,
        DateTimeOffset? receivedBefore = null,
        bool? isRemotelySeen = null,
        bool? isRemotelyFlagged = null,
        string? keyword = null,
        bool? hasAttachments = null) => MailboxEmailSelection.Create(
        scope ?? MailboxScope.NothingReadable,
        senderAddress,
        recipientAddress,
        subjectFragment,
        receivedOnOrAfter,
        receivedBefore,
        isRemotelySeen,
        isRemotelyFlagged,
        keyword,
        hasAttachments);
}
