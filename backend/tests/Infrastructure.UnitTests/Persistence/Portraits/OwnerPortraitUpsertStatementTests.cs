// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Portraits;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Portraits;

/// <summary>
/// A person's portrait is written by one statement, and everything that write promises is in its text: that a second
/// device replaces what the first supplied rather than colliding on the key, that an owner this deployment no longer
/// holds affects no row instead of raising a foreign-key violation, and that the first instant is not moved by a later
/// write. None of that is visible from the port and all of it is decidable without a server, so it is established here.
/// </summary>
public sealed class OwnerPortraitUpsertStatementTests
{
    /// <summary>Two of one person's devices uploading at once is the shape a read-then-insert would fail on, so the second write replaces what the first supplied rather than being refused.</summary>
    [Fact]
    public void Compose_TheWrite_ReplacesTheOctetsOnConflictWithTheOwnersExistingRow()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("ON CONFLICT (\"OwnerId\") DO UPDATE SET", statement, StringComparison.Ordinal);
        Assert.Contains("\"Content\" = EXCLUDED.\"Content\"", statement, StringComparison.Ordinal);
    }

    /// <summary>The first instant records when the person first supplied a picture, so a later write must not carry a new one into it.</summary>
    [Fact]
    public void Compose_AWriteOverAnExistingRow_MovesTheChangedInstantAndLeavesTheFirstOne()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("\"UpdatedAt\" = EXCLUDED.\"UpdatedAt\"", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CreatedAt\" = EXCLUDED", statement, StringComparison.Ordinal);
    }

    /// <summary>
    /// The row is inserted from a select over the owner table, so the existence check and the write are one statement
    /// rather than two decisions a concurrent erasure could fall between — and a caller whose row has gone is answered
    /// by a count of nothing rather than by a constraint violation.
    /// </summary>
    [Fact]
    public void Compose_TheWrite_InsertsFromTheOwnerRowRatherThanUnconditionally()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("FROM \"settings_accounts\"", statement, StringComparison.Ordinal);
        Assert.Contains("WHERE \"Id\" = {0}", statement, StringComparison.Ordinal);
    }

    /// <summary>One instant is written into both columns on an insert, so a row's two timestamps start out equal.</summary>
    [Fact]
    public void Compose_AnInsert_WritesOneInstantIntoBothColumns()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("SELECT {0}, {1}, {2}, {2}", statement, StringComparison.Ordinal);
    }

    /// <summary>Every identifier comes from the model, so the table this writes is the one the mapping declares.</summary>
    [Fact]
    public void Compose_TheWrite_NamesTheTableAndColumnsTheModelStates()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains(
            "INSERT INTO \"owner_portraits\"\n    (\"OwnerId\", \"Content\", \"CreatedAt\", \"UpdatedAt\")",
            statement.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>The octets travel as a parameter, so nothing an uploader supplied is ever part of the statement's text.</summary>
    [Fact]
    public void Compose_TheWrite_CarriesTheOctetsAsAParameterRatherThanAsText()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("{1}", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("'", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NoModelAtAll_IsRefused()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(() => OwnerPortraitUpsertStatement.Compose(null!));
    }

    private static string Composed()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return OwnerPortraitUpsertStatement.Compose(context.Model);
    }
}
