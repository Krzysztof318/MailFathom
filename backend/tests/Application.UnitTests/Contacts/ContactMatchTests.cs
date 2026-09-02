// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers the three answers a lookup of the book can give, and which of them carries a contact.</summary>
public sealed class ContactMatchTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Nobody answering is a count of none and no contact to read.</summary>
    [Fact]
    public void None_NobodyAnswers_CarriesNoContact()
    {
        // Act
        var match = ContactMatch.None;

        // Assert
        Assert.Equal(0, match.MatchCount);
        Assert.Null(match.OnlyMatch);
    }

    /// <summary>Exactly one person answering is the one case a caller may use, so that is the one carrying a contact.</summary>
    [Fact]
    public void Unique_OnePersonAnswers_CarriesThem()
    {
        // Arrange
        var contact = AContact();

        // Act
        var match = ContactMatch.Unique(contact);

        // Assert
        Assert.Equal(1, match.MatchCount);
        Assert.Same(contact, match.OnlyMatch);
    }

    /// <summary>Several answering carries how many and nothing about any of them.</summary>
    [Fact]
    public void Several_SeveralAnswer_CarriesTheCountAndNoContact()
    {
        // Act
        var match = ContactMatch.Several(3);

        // Assert
        Assert.Equal(3, match.MatchCount);
        Assert.Null(match.OnlyMatch);
    }

    /// <summary>A count below two is a unique answer or none, and reporting it as ambiguous would hide the contact.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Several_ACountBelowTwo_IsRefused(int matchCount) =>

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactMatch.Several(matchCount));

    private static Contact AContact()
    {
        EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address);

        return Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Recorded)),
            ContactDisplayName.Create("Anna Kowalska"),
            [address],
            address,
            note: null,
            ContactOrigin.Asserted,
            Recorded,
            Recorded);
    }
}
