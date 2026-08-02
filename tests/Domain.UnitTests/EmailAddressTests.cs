// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests;

public sealed class EmailAddressTests
{
    /// <summary>Two addresses that differ only in case must compare equal, which is what search and future deduplication depend on.</summary>
    [Theory]
    [InlineData("Anna.Kowalska@Example.Test")]
    [InlineData("anna.kowalska@example.test")]
    [InlineData("ANNA.KOWALSKA@EXAMPLE.TEST")]
    [InlineData("  anna.kowalska@example.test  ")]
    public void TryCreate_AddressWrittenDifferently_ProducesOneComparisonForm(string writtenAddress)
    {
        // Arrange
        const string expectedNormalizedAddress = "ANNA.KOWALSKA@EXAMPLE.TEST";

        // Act
        var created = EmailAddress.TryCreate(displayName: null, writtenAddress, out var address);

        // Assert
        Assert.True(created);
        Assert.Equal(expectedNormalizedAddress, address.NormalizedAddress);
    }

    /// <summary>The written form is kept alongside the comparison form, because only one of them is meant to be shown.</summary>
    [Fact]
    public void TryCreate_MixedCaseAddress_KeepsWhatTheMessageWrote()
    {
        // Arrange
        const string writtenAddress = "Anna.Kowalska@Example.Test";

        // Act
        EmailAddress.TryCreate("Anna Kowalska", writtenAddress, out var address);

        // Assert
        Assert.Equal(writtenAddress, address.Address);
        Assert.Equal("Anna Kowalska", address.DisplayName);
    }

    /// <summary>A quoted local part may contain a space and an at-sign, and neither may be edited out from under the sender.</summary>
    [Theory]
    [InlineData("\"John Smith\"@example.com")]
    [InlineData("\"a@b\"@example.com")]
    [InlineData("\"anna kowalska\"@example.test")]
    public void TryCreate_QuotedLocalPart_KeepsItExactlyAsWritten(string writtenAddress)
    {
        // Act
        var created = EmailAddress.TryCreate(displayName: null, writtenAddress, out var address);

        // Assert
        Assert.True(created);
        Assert.Equal(writtenAddress, address.Address);
        Assert.Equal(writtenAddress.ToUpperInvariant(), address.NormalizedAddress);
    }

    /// <summary>Identity is the comparison form alone, so the casing one sender chose cannot split a participant in two.</summary>
    [Fact]
    public void Equals_AddressesDifferingOnlyInPresentation_AreOneParticipant()
    {
        // Arrange
        EmailAddress.TryCreate("Anna Kowalska", "Anna.Kowalska@Example.Test", out var written);
        EmailAddress.TryCreate(displayName: null, "anna.kowalska@example.test", out var echoed);

        // Act
        var distinctAddresses = new[] { written, echoed }.Distinct().ToArray();

        // Assert
        Assert.Equal(written, echoed);
        Assert.Equal(written.GetHashCode(), echoed.GetHashCode());
        Assert.Single(distinctAddresses);
    }

    /// <summary>Two different mailboxes stay two, so normalization merges casing and nothing else.</summary>
    [Fact]
    public void Equals_DifferentMailboxes_StayDistinct()
    {
        // Arrange
        EmailAddress.TryCreate(displayName: null, "anna@example.test", out var anna);
        EmailAddress.TryCreate(displayName: null, "bob@example.test", out var bob);

        // Assert
        Assert.NotEqual(anna, bob);
    }

    /// <summary>A display name that carries only formatting, or none at all, is absent rather than blank.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void TryCreate_DisplayNameWithNothingReadable_RecordsNone(string? displayName)
    {
        // Act
        EmailAddress.TryCreate(displayName, "anna@example.test", out var address);

        // Assert
        Assert.Null(address.DisplayName);
    }

    /// <summary>A display name may not smuggle a line break into anything that later writes it on one line.</summary>
    [Fact]
    public void TryCreate_DisplayNameCarryingControlCharacters_RemovesThem()
    {
        // Act
        EmailAddress.TryCreate("Anna\r\nKowalska", "anna@example.test", out var address);

        // Assert
        Assert.Equal("AnnaKowalska", address.DisplayName);
    }

    /// <summary>A malformed address is refused rather than repaired, so nothing an author never wrote reaches a filter.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("anna.kowalska")]
    [InlineData("@example.test")]
    [InlineData("anna@")]
    [InlineData("anna@@example.test")]
    [InlineData("anna@example.test@second.test")]
    [InlineData("anna kowalska@example.test")]
    [InlineData("anna@example test")]
    [InlineData("\"\"@example.test")]
    [InlineData("anna\u0001@example.test")]
    public void TryCreate_MalformedAddress_IsRefused(string? writtenAddress)
    {
        // Act
        var created = EmailAddress.TryCreate(displayName: null, writtenAddress, out var address);

        // Assert
        Assert.False(created);
        Assert.Equal(default, address);
    }

    /// <summary>The role is what makes an address answerable: "wrote this" and "was copied on this" are different questions about one person.</summary>
    [Fact]
    public void EmailParticipant_SameAddressInTwoHeaders_StaysTwoParticipants()
    {
        // Arrange
        EmailAddress.TryCreate("Anna Kowalska", "anna@example.test", out var address);

        // Act
        var author = new EmailParticipant(EmailAddressRole.From, address);
        var copied = new EmailParticipant(EmailAddressRole.Cc, address);

        // Assert
        Assert.NotEqual(author, copied);
        Assert.Equal(address, author.Address);
        Assert.Equal(EmailAddressRole.Cc, copied.Role);
    }
}
