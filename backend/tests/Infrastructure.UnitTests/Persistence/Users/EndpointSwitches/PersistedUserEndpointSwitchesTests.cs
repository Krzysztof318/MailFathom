// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users.EndpointSwitches;

/// <summary>
/// Covers the decisions this store takes before it reaches a database. Which rows trail, what a derivation reads, and
/// what it writes are statements, and the integration suite proves those against a real server.
/// </summary>
public sealed class PersistedUserEndpointSwitchesTests
{
    /// <summary>A connection string nothing listens on, because every guard refuses before anything is opened.</summary>
    private const string UnreachedDatabase = "Host=localhost;Port=1;Database=mailfathom;Username=mailfathom";

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadTrailingAsync_ALimitThatReadsNothing_IsRejectedAsAnArgument(int limit)
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create(UnreachedDatabase);

        // Act
        var rejected = await Record.ExceptionAsync(
            () => StoreOver(dataSource).ReadTrailingAsync(after: null, limit, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(rejected);
    }

    [Fact]
    public async Task DeriveAgainAsync_AUserNamingNobody_IsRejectedAsAnArgument()
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create(UnreachedDatabase);

        // Act
        var rejected = await Record.ExceptionAsync(
            () => StoreOver(dataSource).DeriveAgainAsync(default, _ => null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("user", Assert.IsType<ArgumentException>(rejected).ParamName);
    }

    /// <summary>A derivation that states no way to derive has nothing to write, and saying so is this side's to do rather than a null dereference inside a transaction.</summary>
    [Fact]
    public async Task DeriveAgainAsync_NoDerivation_IsRejectedAsAnArgument()
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create(UnreachedDatabase);
        var user = UserId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

        // Act
        var rejected = await Record.ExceptionAsync(
            () => StoreOver(dataSource).DeriveAgainAsync(user, derive: null!, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("derive", Assert.IsType<ArgumentNullException>(rejected).ParamName);
    }

    private static PersistedUserEndpointSwitches StoreOver(NpgsqlDataSource dataSource) =>
        new(() => dataSource, new DatabaseCommandTimeout(TimeSpan.FromSeconds(30)));
}
