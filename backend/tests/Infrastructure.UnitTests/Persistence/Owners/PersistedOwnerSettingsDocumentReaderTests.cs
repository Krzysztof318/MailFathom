// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Owners;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>
/// Covers the one decision this reader makes before it reaches a database. Everything else it does needs a real
/// server and is proved by the integration suite, but the guard runs first and its absence would not fail there: an
/// owner naming nobody would match no row, and the port answers an absent row as an owner this deployment holds no
/// record of — so a caller acting for nobody would be told the deployment does not know them.
/// </summary>
public sealed class PersistedOwnerSettingsDocumentReaderTests
{
    /// <summary>A connection string nothing listens on, because the guard refuses before anything is opened.</summary>
    private const string UnreachedDatabase = "Host=localhost;Port=1;Database=mailfathom;Username=mailfathom";

    [Fact]
    public async Task ReadAsync_AnOwnerNamingNobody_IsRejectedAsAnArgument()
    {
        // Arrange
        await using var dataSource = NpgsqlDataSource.Create(UnreachedDatabase);
        var reader = new PersistedOwnerSettingsDocumentReader(
            dataSource,
            new DatabaseCommandTimeout(TimeSpan.FromSeconds(30)));

        // Act
        var rejected = await Record.ExceptionAsync(
            () => reader.ReadAsync(default, TestContext.Current.CancellationToken));

        // Assert
        var argument = Assert.IsType<ArgumentException>(rejected);
        Assert.Equal("owner", argument.ParamName);

        // The connection string points at a port nothing listens on, so anything that reached the database would have
        // come back as this instead. That it did not is what says the guard ran first.
        Assert.IsNotType<OwnerSettingsUnreadableException>(rejected);
    }
}
