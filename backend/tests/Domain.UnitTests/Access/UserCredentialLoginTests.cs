// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers how a typed login splits into an organization and a username, and the one canonical form it takes.</summary>
public sealed class UserCredentialLoginTests
{
    [Fact]
    public void TryRead_ALoginNamingAnOrganization_FoldsEachHalfByItsOwnRule()
    {
        // Act
        var read = UserCredentialLogin.TryRead(" TestFirma/ Jan ", out var login);

        // Assert
        Assert.True(read);
        Assert.Equal("TESTFIRMA", login.Organization.Value);
        Assert.Equal("jan", login.Username.Value);
        Assert.Equal("TESTFIRMA/jan", login.Value);
    }

    [Fact]
    public void TryRead_ALoginNamingNoOrganization_IsTheUsernameAlone()
    {
        // Act
        var read = UserCredentialLogin.TryRead("Jan", out var login);

        // Assert
        Assert.True(read);
        Assert.False(login.Organization.IsSpecified);
        Assert.Equal("jan", login.Value);
    }

    /// <summary>The same username in no organization and in one is two logins, which is what lets two companies each have a <c>jan</c>.</summary>
    [Fact]
    public void TryRead_OneUsernameInAndOutOfAnOrganization_ReadsAsTwoLogins()
    {
        // Act
        UserCredentialLogin.TryRead("jan", out var unscoped);
        UserCredentialLogin.TryRead("TESTFIRMA/jan", out var scoped);

        // Assert
        Assert.NotEqual(unscoped, scoped);
        Assert.Equal(unscoped.Username, scoped.Username);
    }

    /// <summary>The login splits at its first slash, so a second one lands in the username, which refuses it.</summary>
    [Theory]
    [InlineData("/jan")]
    [InlineData("TESTFIRMA/")]
    [InlineData("TEST_FIRMA/jan")]
    [InlineData("TESTFIRMA/jan/kowalski")]
    [InlineData("TESTFIRMA/jan:kowalski")]
    [InlineData("")]
    [InlineData(null)]
    public void TryRead_AnUnusableLogin_IsRefused(string? written)
    {
        // Act
        var read = UserCredentialLogin.TryRead(written, out var login);

        // Assert
        Assert.False(read);
        Assert.False(login.IsSpecified);
    }
}
