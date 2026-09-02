// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Preferences;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Preferences;

/// <summary>
/// A person's preferences are written by one statement, and everything that write promises is in its text: that a
/// second device replaces what the first wrote rather than colliding on the key, that an owner this deployment no
/// longer holds affects no row instead of raising a foreign-key violation, that the first instant is not moved by a
/// later write, and that the document reaches a <c>jsonb</c> column as one. None of that is visible from the port and
/// all of it is decidable without a server, so it is established here.
/// </summary>
public sealed class ClientPreferencesUpsertStatementTests
{
    /// <summary>Two of one person's devices saving at once is the shape a read-then-insert would fail on, so the second write replaces what the first wrote rather than being refused.</summary>
    [Fact]
    public void Compose_TheWrite_ReplacesTheDocumentOnConflictWithTheOwnersExistingRow()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("ON CONFLICT (\"OwnerId\") DO UPDATE SET", statement, StringComparison.Ordinal);
        Assert.Contains("\"Document\" = EXCLUDED.\"Document\"", statement, StringComparison.Ordinal);
    }

    /// <summary>The first instant records when the person first set anything about their client, so a later write must not carry a new one into it.</summary>
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

    /// <summary>The column is <c>jsonb</c> and the parameter is text, so without the cast PostgreSQL refuses the insert.</summary>
    [Fact]
    public void Compose_TheWrite_CastsTheDocumentParameterToTheColumnsType()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("{1}::jsonb", statement, StringComparison.Ordinal);
    }

    /// <summary>One instant is written into both columns on an insert, so a row's two timestamps start out equal.</summary>
    [Fact]
    public void Compose_AnInsert_WritesOneInstantIntoBothColumns()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains("SELECT {0}, {1}::jsonb, {2}, {2}", statement, StringComparison.Ordinal);
    }

    /// <summary>Every identifier comes from the model, so the table this writes is the one the mapping declares.</summary>
    [Fact]
    public void Compose_TheWrite_NamesTheTableAndColumnsTheModelStates()
    {
        // Act
        var statement = Composed();

        // Assert
        Assert.Contains(
            "INSERT INTO \"client_preferences\"\n    (\"OwnerId\", \"Document\", \"CreatedAt\", \"UpdatedAt\")",
            statement.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NoModelAtAll_IsRefused()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(() => ClientPreferencesUpsertStatement.Compose(null!));
    }

    private static string Composed()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return ClientPreferencesUpsertStatement.Compose(context.Model);
    }
}
