// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the reader a measurement arranges an account that believes no server with.</summary>
/// <remarks>
/// A reader that answered anything but <see cref="TrustedAuthenticationAuthority.None" /> would send extraction down
/// the trusted-header path in every measurement that uses it, and the number would then be about a branch nobody meant
/// to measure rather than about the parse.
/// </remarks>
public sealed class NoTrustedAuthenticationTests
{
    [Fact]
    public void GetTrustedAuthority_AnyAccount_NamesNoServer()
    {
        // Arrange
        var authorities = new NoTrustedAuthentication();

        // Act
        var authority = authorities.GetTrustedAuthority(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(TrustedAuthenticationAuthority.None, authority);
        Assert.False(authority.NamesAServer);
    }
}
