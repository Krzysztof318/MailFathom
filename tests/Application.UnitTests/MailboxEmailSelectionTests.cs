// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests;

/// <summary>Covers what the structural filters every mailbox read shares validate and normalize.</summary>
public sealed class MailboxEmailSelectionTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

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
            hasAttachments: null));
    }

    private static MailboxEmailSelection SelectionWith(
        MailboxScope? scope = null,
        string? senderAddress = null,
        string? recipientAddress = null,
        string? subjectFragment = null,
        DateTimeOffset? receivedOnOrAfter = null,
        DateTimeOffset? receivedBefore = null,
        bool? isRemotelySeen = null,
        bool? hasAttachments = null) => MailboxEmailSelection.Create(
        scope ?? MailboxScope.Unrestricted,
        senderAddress,
        recipientAddress,
        subjectFragment,
        receivedOnOrAfter,
        receivedBefore,
        isRemotelySeen,
        hasAttachments);
}
