// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the identity that says whose mail an account holds.</summary>
/// <remarks>
/// The value decides what a caller may read, so the one thing worth proving about it is that no value which names
/// nobody can pass for one: a scope resolved for an unnamed user would compare equal to every other unnamed user.
/// </remarks>
public sealed class MailUserIdTests
{
    [Fact]
    public void Create_AGeneratedIdentifier_CarriesItAndNamesAUser()
    {
        // Arrange
        var value = new Guid("0198f0aa-0000-7000-8000-0000000000a1");

        // Act
        var user = MailUserId.Create(value);

        // Assert
        Assert.Equal(value, user.Value);
        Assert.True(user.IsSpecified);
        Assert.Equal(value.ToString(), user.ToString());
    }

    /// <summary>The empty identifier is what an unset column and an unread configuration value both look like.</summary>
    [Fact]
    public void Create_TheEmptyIdentifier_IsRejected()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => MailUserId.Create(Guid.Empty));
    }

    /// <summary>
    /// The struct default cannot be refused at construction, so it has to say for itself that it names nobody —
    /// otherwise a field nobody assigned would read as a user, and every one of them as the same user.
    /// </summary>
    [Fact]
    public void IsSpecified_TheStructDefault_NamesNobody()
    {
        // Arrange & Act
        var unspecified = default(MailUserId);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal(Guid.Empty, unspecified.Value);
    }

    /// <summary>Two accounts belong to one user when the identifiers agree, which is what the resolution compares.</summary>
    [Fact]
    public void Equals_TwoIdentitiesOverOneIdentifier_AreTheSameUser()
    {
        // Arrange
        var value = new Guid("0198f0aa-0000-7000-8000-0000000000a2");

        // Act
        var user = MailUserId.Create(value);
        var sameUser = MailUserId.Create(value);

        // Assert
        Assert.Equal(user, sameUser);
        Assert.NotEqual(user, MailUserId.Create(new Guid("0198f0aa-0000-7000-8000-0000000000a3")));
    }
}
