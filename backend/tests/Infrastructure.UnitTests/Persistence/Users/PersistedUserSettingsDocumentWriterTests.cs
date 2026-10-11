// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>
/// Covers the decisions this writer takes before it reaches a database. Everything else it does needs a real server and
/// is proved by the integration suite — including what it refuses of the record itself, which is composed inside the
/// write's transaction under the policies read there and so cannot be judged before one is open.
/// <see cref="UserSettingsCommitRulesTests" /> covers those rules on their own.
/// </summary>
public sealed class PersistedUserSettingsDocumentWriterTests
{
    /// <summary>A connection string nothing listens on, because every guard refuses before anything is opened.</summary>
    private const string UnreachedDatabase = "Host=localhost;Port=1;Database=mailfathom;Username=mailfathom";

    [Fact]
    public async Task CommitAsync_AUserNamingNobody_IsRejectedAsAnArgument()
    {
        // Act
        var rejected = await CommitAsync(default, expectedVersion: 1, ComposeNothing);

        // Assert
        var argument = Assert.IsType<ArgumentException>(rejected);

        Assert.Equal("user", argument.ParamName);
    }

    /// <summary>A write that states no way to compose its record has nothing to commit, and saying so is this side's to do rather than a null dereference inside a transaction.</summary>
    [Fact]
    public async Task CommitAsync_NoComposition_IsRejectedAsAnArgument()
    {
        // Act
        var rejected = await CommitAsync(SyntheticUser, expectedVersion: 1, compose: null!);

        // Assert
        var argument = Assert.IsType<ArgumentNullException>(rejected);

        Assert.Equal("compose", argument.ParamName);
    }

    /// <summary>A version no row can stand at is a candidate this build composed wrongly rather than anything the database has an opinion about.</summary>
    [Fact]
    public async Task CommitAsync_ANegativeExpectedVersion_IsRejectedAsAnArgument()
    {
        // Act
        var rejected = await CommitAsync(SyntheticUser, expectedVersion: -1, ComposeNothing);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(rejected);
    }

    /// <summary>A user this suite writes for, which no statement here ever reaches.</summary>
    private static UserId SyntheticUser { get; } =
        UserId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static UserRecordCandidate? ComposeNothing(GoverningSettingsPolicies policies) => null;

    private static async Task<Exception?> CommitAsync(
        UserId user,
        long expectedVersion,
        Func<GoverningSettingsPolicies, UserRecordCandidate?> compose)
    {
        await using var dataSource = NpgsqlDataSource.Create(UnreachedDatabase);

        var writer = new PersistedUserSettingsDocumentWriter(
            dataSource,
            new DatabaseCommandTimeout(TimeSpan.FromSeconds(30)),
            new FakeTimeProvider());

        return await Record.ExceptionAsync(
            () => writer.CommitAsync(
                user,
                expectedVersion,
                compose,
                TestContext.Current.CancellationToken));
    }
}
