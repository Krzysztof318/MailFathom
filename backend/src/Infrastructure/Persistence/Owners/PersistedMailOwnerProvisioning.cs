// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Writes the envelope of an owner a deployment declares, and nothing inside their document.</summary>
/// <remarks>
/// <para>
/// Both statements are single and conditional, which is what makes them safe against the race they actually meet: two
/// replicas of one deployment start at the same moment and reconcile the same roster. An insert guarded by
/// <c>ON CONFLICT</c> on the primary key leaves the loser having written nothing rather than raising, and an update
/// that names the label it is replacing writes no row when the label is already the one declared.
/// </para>
/// <para>
/// The document is provisioned as the empty object and is never written here. An owner read from configuration is
/// served from their declaration, so filling the column would be the adoption that stops the file reaching them —
/// which is a deliberate act with a command of its own rather than something a start does on their behalf.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailOwnerProvisioning(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IMailOwnerProvisioning
{
    /// <summary>The document an owner's row is provisioned with, which is the empty record rather than their declaration.</summary>
    private const string EmptyDocument = "{}";

    /// <inheritdoc />
    public async Task ProvisionAsync(MailOwnerId owner, string displayName, CancellationToken cancellationToken)
    {
        var ownerId = RequireNamed(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var provisionedAt = timeProvider.GetUtcNow();

        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO settings_accounts ("Id", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt", "DocumentWrittenAtRuntime")
             VALUES ({ownerId}, {displayName}, CAST({EmptyDocument} AS jsonb), 1, {provisionedAt}, {provisionedAt}, FALSE)
             ON CONFLICT ("Id") DO NOTHING
             """,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RelabelAsync(MailOwnerId owner, string displayName, CancellationToken cancellationToken)
    {
        var ownerId = RequireNamed(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // The version is deliberately left where it is. It guards the document a writer composes over, and the label is
        // not part of that document, so stepping it here would refuse a write that had read the record correctly.
        await dbContext.OwnerAccounts
            .Where(record => record.Id == ownerId && record.DisplayName != displayName)
            .ExecuteUpdateAsync(
                record => record.SetProperty(owner => owner.DisplayName, displayName),
                cancellationToken);
    }

    private static Guid RequireNamed(MailOwnerId owner) =>
        owner.IsSpecified
            ? owner.Value
            : throw new ArgumentException("An owner record is provisioned for a named owner.", nameof(owner));
}
