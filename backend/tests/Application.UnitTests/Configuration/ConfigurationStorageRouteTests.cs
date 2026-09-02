// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using Xunit;

namespace MailFathom.Application.UnitTests.Configuration;

/// <summary>
/// Covers the value a route is. The set is closed and its names are what a refusal and a record are written against,
/// so the tests state that the names are distinct, that the struct default is reported rather than mistaken for a
/// store, and that nothing here reads a name from outside the process.
/// </summary>
public sealed class ConfigurationStorageRouteTests
{
    /// <summary>A name identifies its store, so two stores sharing one would make a record ambiguous.</summary>
    [Fact]
    public void All_DeclaredRoutes_HaveDistinctNames()
    {
        // Act
        var names = ConfigurationStorageRoute.All.Select(route => route.Name).ToArray();

        // Assert
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Every declared route is a route, which is what separates the set from the reachable struct default.</summary>
    [Fact]
    public void All_DeclaredRoutes_AreSpecified()
    {
        // Assert
        Assert.All(ConfigurationStorageRoute.All, route => Assert.True(route.IsSpecified));
    }

    /// <summary>The two stores this build persists into are the whole of the set, and a third one is a reviewed change to it.</summary>
    [Fact]
    public void All_Routes_AreTheRootDocumentAndTheOwnerAccountsStore()
    {
        // Assert
        Assert.Equal(
            [ConfigurationStorageRoute.RootDocument, ConfigurationStorageRoute.OwnerAccounts],
            ConfigurationStorageRoute.All);
    }

    /// <summary>A route's name reaches an operator's record, so it is what the value reads as.</summary>
    [Fact]
    public void ToString_DeclaredRoute_IsItsName()
    {
        // Assert
        Assert.Equal("owner-accounts", ConfigurationStorageRoute.OwnerAccounts.ToString());
    }

    /// <summary>The struct default is reachable and is not a store; it reports itself rather than pretending to be one.</summary>
    [Fact]
    public void Default_UnspecifiedValue_ReportsItselfRatherThanNamingAStore()
    {
        // Arrange
        var unspecified = default(ConfigurationStorageRoute);

        // Act
        var naming = Record.Exception(() => unspecified.Name);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.IsType<InvalidOperationException>(naming);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }
}
