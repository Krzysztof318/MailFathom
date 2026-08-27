// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Reads one owner's record out of the single <c>settings_accounts</c> row that holds it.</summary>
/// <remarks>
/// <para>
/// The lookup is by primary key, which is what the routing decision buys: one owner is one row, so an owner-scoped
/// view costs one seek rather than a scan of every owner's settings or a value-per-key query. Nothing here reads into
/// the document — the column travels as the text a binder will parse — because what it contains is the configuration
/// layer's to interpret and this table exists to hand that layer a row.
/// </para>
/// <para>
/// The version travels with it rather than being read again by whoever writes next. A writer that re-read the version
/// after deciding its change would be stating a number it had not composed over, which is exactly the race the version
/// exists to refuse.
/// </para>
/// <para>
/// The document is carried whatever its size, unlike the deployment's, and the difference is where the ceiling has to
/// hold. <see cref="OwnerSettingsDocument.MaximumOctets" /> bounds what MailFathom binds, and every write is judged
/// against it before it commits, so a row past it can only be one somebody wrote into the database by hand — and it is
/// refused where the expansion happens rather than here, with the refusal reaching the caller who asked for the owner.
/// The deployment's own document is read before any endpoint is open and bounded in the statement for exactly that
/// reason: there, an oversized row is a start with no message rather than an answer somebody receives.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedOwnerSettingsDocumentReader(MailFathomDbContext dbContext) : IOwnerSettingsDocumentReader
{
    /// <inheritdoc />
    public async Task<OwnerSettingsDocument?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An owner record is read for an owner, and the value names nobody.", nameof(owner));
        }

        var ownerValue = owner.Value;

        var record = await dbContext.OwnerAccounts
            .AsNoTracking()
            .Where(candidate => candidate.Id == ownerValue)
            .Select(candidate => new
            {
                candidate.DisplayName,
                candidate.Document,
                candidate.Version,
                candidate.DocumentWrittenAtRuntime,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return record is null
            ? null
            : new OwnerSettingsDocument(
                owner,
                record.DisplayName,
                record.Document,
                record.Version,
                record.DocumentWrittenAtRuntime);
    }
}
