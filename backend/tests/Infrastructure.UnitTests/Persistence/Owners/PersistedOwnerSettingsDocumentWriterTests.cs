// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Owners;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>
/// Covers the decisions this writer takes before it reaches a database. Everything else it does needs a real server and
/// is proved by the integration suite, but each of these refuses a candidate no statement should be issued for — and
/// none of them would fail there in a way anybody could act on: the column would take a document whose root is not an
/// object, and the next read would then refuse a row nobody could correct through this port.
/// </summary>
public sealed class PersistedOwnerSettingsDocumentWriterTests
{
    /// <summary>A connection string nothing listens on, because every guard refuses before anything is opened.</summary>
    private const string UnreachedDatabase = "Host=localhost;Port=1;Database=mailfathom;Username=mailfathom";

    [Fact]
    public async Task CommitAsync_AnOwnerNamingNobody_IsRejectedAsAnArgument()
    {
        // Act
        var rejected = await CommitAsync(default, "{}", expectedVersion: 1);

        // Assert
        AssertRefusedBeforeTheDatabase(rejected, "owner");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CommitAsync_ACandidateThatIsNoDocumentAtAll_IsRejectedAsAnArgument(string json)
    {
        // Act
        var rejected = await CommitAsync(SyntheticOwner, json, expectedVersion: 1);

        // Assert
        AssertRefusedBeforeTheDatabase(rejected, "json");
    }

    [Fact]
    public async Task CommitAsync_NoCandidateAtAll_IsRejectedAsAnArgument()
    {
        // Act
        var rejected = await CommitAsync(SyntheticOwner, json: null!, expectedVersion: 1);

        // Assert
        var argument = Assert.IsType<ArgumentNullException>(rejected);

        Assert.Equal("json", argument.ParamName);
        Assert.IsNotType<OwnerSettingsUnwritableException>(rejected);
    }

    /// <summary>The column's own jsonb cast refuses exactly this, and the refusal belongs on the side that composed the document.</summary>
    [Fact]
    public async Task CommitAsync_ACandidateThatIsNotJson_IsRejectedWithoutIssuingAStatement()
    {
        // Act
        var rejected = await CommitAsync(SyntheticOwner, "not json", expectedVersion: 1);

        // Assert
        var argument = AssertRefusedBeforeTheDatabase(rejected, "json");

        Assert.Contains("not JSON", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>The column would store it and the next read would refuse it, so only this side can stop the row being written.</summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("\"a record\"")]
    [InlineData("42")]
    public async Task CommitAsync_ACandidateWhoseRootIsNotAnObject_IsRejectedWithoutIssuingAStatement(string json)
    {
        // Act
        var rejected = await CommitAsync(SyntheticOwner, json, expectedVersion: 1);

        // Assert
        var argument = AssertRefusedBeforeTheDatabase(rejected, "json");

        Assert.Contains("root is not an object", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bound is measured as the database stores the document rather than as the candidate was composed, because a
    /// write permitted past what the bind accepts would persist a record the next read refuses — and the owner would be
    /// locked out by a change that had been accepted.
    /// </summary>
    [Fact]
    public async Task CommitAsync_ACandidatePastWhatTheNextReadWouldBind_IsRejectedNamingBothMeasurements()
    {
        // Arrange
        var oversized = DocumentOccupying(OwnerSettingsDocument.MaximumOctets);

        // Act
        var rejected = await CommitAsync(SyntheticOwner, oversized, expectedVersion: 1);

        // Assert
        var argument = AssertRefusedBeforeTheDatabase(rejected, "json");

        Assert.Contains(
            OwnerSettingsDocument.MaximumOctets.ToString(CultureInfo.InvariantCulture),
            argument.Message,
            StringComparison.Ordinal);
    }

    /// <summary>A version no row can stand at is a candidate this build composed wrongly rather than anything the database has an opinion about.</summary>
    [Fact]
    public async Task CommitAsync_ANegativeExpectedVersion_IsRejectedAsAnArgument()
    {
        // Act
        var rejected = await CommitAsync(SyntheticOwner, "{}", expectedVersion: -1);

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(rejected);
        Assert.IsNotType<OwnerSettingsUnwritableException>(rejected);
    }

    /// <summary>An owner this suite writes for, which no statement here ever reaches.</summary>
    private static MailOwnerId SyntheticOwner { get; } =
        MailOwnerId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    /// <summary>Composes a document whose stored rendering is past the bound, which is what the rule measures.</summary>
    /// <remarks>The compact form is smaller than the rendering, so the padding is sized against the bound itself rather than against what this string occupies.</remarks>
    private static string DocumentOccupying(int octets) =>
        $$"""{"Padding":"{{new string('a', octets)}}"}""";

    private static async Task<Exception?> CommitAsync(
        MailOwnerId owner,
        string json,
        long expectedVersion)
    {
        await using var dataSource = NpgsqlDataSource.Create(UnreachedDatabase);

        var writer = new PersistedOwnerSettingsDocumentWriter(
            dataSource,
            new DatabaseCommandTimeout(TimeSpan.FromSeconds(30)),
            new FakeTimeProvider());

        return await Record.ExceptionAsync(
            () => writer.CommitAsync(owner, json, expectedVersion, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Asserts the guard ran rather than the database. The connection string points at a port nothing listens on, so
    /// anything that reached it would have come back as the write's own exception instead.
    /// </summary>
    private static ArgumentException AssertRefusedBeforeTheDatabase(Exception? rejected, string parameter)
    {
        var argument = Assert.IsType<ArgumentException>(rejected);

        Assert.Equal(parameter, argument.ParamName);
        Assert.IsNotType<OwnerSettingsUnwritableException>(rejected);

        return argument;
    }
}
