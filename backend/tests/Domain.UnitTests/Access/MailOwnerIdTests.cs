// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the identity that says whose mail an account holds.</summary>
/// <remarks>
/// The value decides what a caller may read, so the one thing worth proving about it is that no value which names
/// nobody can pass for one: a scope resolved for an unnamed owner would compare equal to every other unnamed owner.
/// </remarks>
public sealed class MailOwnerIdTests
{
    [Fact]
    public void Create_AGeneratedIdentifier_CarriesItAndNamesAnOwner()
    {
        // Arrange
        var value = new Guid("0198f0aa-0000-7000-8000-0000000000a1");

        // Act
        var owner = MailOwnerId.Create(value);

        // Assert
        Assert.Equal(value, owner.Value);
        Assert.True(owner.IsSpecified);
        Assert.Equal(value.ToString(), owner.ToString());
    }

    /// <summary>The empty identifier is what an unset column and an unread configuration value both look like.</summary>
    [Fact]
    public void Create_TheEmptyIdentifier_IsRejected()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => MailOwnerId.Create(Guid.Empty));
    }

    /// <summary>
    /// The struct default cannot be refused at construction, so it has to say for itself that it names nobody —
    /// otherwise a field nobody assigned would read as an owner, and every one of them as the same owner.
    /// </summary>
    [Fact]
    public void IsSpecified_TheStructDefault_NamesNobody()
    {
        // Arrange & Act
        var unspecified = default(MailOwnerId);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal(Guid.Empty, unspecified.Value);
    }

    /// <summary>Two accounts belong to one owner when the identifiers agree, which is what the resolution compares.</summary>
    [Fact]
    public void Equals_TwoIdentitiesOverOneIdentifier_AreTheSameOwner()
    {
        // Arrange
        var value = new Guid("0198f0aa-0000-7000-8000-0000000000a2");

        // Act
        var owner = MailOwnerId.Create(value);
        var sameOwner = MailOwnerId.Create(value);

        // Assert
        Assert.Equal(owner, sameOwner);
        Assert.NotEqual(owner, MailOwnerId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000a3")));
    }
}
