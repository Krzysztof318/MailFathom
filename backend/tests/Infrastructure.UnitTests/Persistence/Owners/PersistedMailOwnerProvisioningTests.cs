// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Owners;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Owners;

/// <summary>
/// Covers the one decision in this writer that is a statement rather than a round trip: which rows a relabel writes to.
/// Everything else it does needs a real server and is the integration suite's, but a predicate the provider cannot
/// translate raises rather than answering, and the first place that would surface is an operator's own rename.
/// </summary>
public sealed class PersistedMailOwnerProvisioningTests
{
    private static readonly Guid Owner = new("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// The label is unique across the deployment, so the write is conditional on nobody else carrying it. A statement
    /// without that guard would meet the column's index instead, and PostgreSQL's own unique-violation sentence would
    /// reach the operator in place of the refusal the roster composes.
    /// </summary>
    [Fact]
    public void RowsToRelabel_ALabel_WritesOnlyWhileNoOtherOwnerCarriesIt()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedMailOwnerProvisioning
            .RowsToRelabel(context.OwnerAccounts, Owner, "alex")
            .ToQueryString();

        // Assert
        Assert.Contains("EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("\"Id\" <> ", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// A label that is already the row's is written by nobody, which is what lets the read afterwards report the row as
    /// carrying it rather than as contested. The predicate is where that is decided, so the assertion names the
    /// comparison against the label rather than the presence of an inequality — the correlated guard above contributes
    /// one of its own, and asserting that shape alone would pass with this comparison deleted.
    /// </summary>
    [Fact]
    public void RowsToRelabel_TheLabelTheRowAlreadyCarries_SelectsNothingToWrite()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = PersistedMailOwnerProvisioning
            .RowsToRelabel(context.OwnerAccounts, Owner, "alex")
            .ToQueryString();

        // Assert
        Assert.Contains("\"DisplayName\" <> ", sql, StringComparison.Ordinal);
    }

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
