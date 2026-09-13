// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the one form a short name is stored and compared in, and what it refuses.</summary>
public sealed class OrganizationShortNameTests
{
    [Theory]
    [InlineData("testfirma", "TESTFIRMA")]
    [InlineData("  acme-2  ", "ACME-2")]
    public void TryCreate_AWrittenShortName_IsFoldedToUpperCase(string written, string canonical)
    {
        // Act
        var created = OrganizationShortName.TryCreate(written, out var shortName);

        // Assert
        Assert.True(created);
        Assert.Equal(canonical, shortName.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TEST/FIRMA")]
    [InlineData("TEST FIRMA")]
    [InlineData("TEST_FIRMA")]
    [InlineData("FIRMA:")]
    [InlineData("ŻABKA")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    [InlineData(null)]
    public void TryCreate_AShortNameOutsideTheAcceptedForm_IsRefused(string? written)
    {
        // Act
        var created = OrganizationShortName.TryCreate(written, out var shortName);

        // Assert
        Assert.False(created);
        Assert.False(shortName.IsSpecified);
    }

    [Fact]
    public void TryCreate_AShortNameAtTheBound_IsAccepted()
    {
        // Act
        var created = OrganizationShortName.TryCreate(new string('A', OrganizationShortName.MaximumLength), out _);

        // Assert
        Assert.True(created);
    }
}
