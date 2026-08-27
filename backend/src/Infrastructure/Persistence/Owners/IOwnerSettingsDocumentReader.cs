// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Reads one owner's persisted record.</summary>
/// <remarks>
/// One read answers for one owner and never for the deployment. That is the whole of the contract's shape: an
/// owner-scoped view is a view of one person's record, so the read is a key lookup rather than a query, and a caller
/// holding several owners asks for each rather than being handed a page of other people's documents to filter.
/// </remarks>
public interface IOwnerSettingsDocumentReader
{
    /// <summary>Reads the record of one owner.</summary>
    /// <param name="owner">The owner whose record is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owner's record, or <see langword="null" /> when this deployment holds no such owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="OwnerSettingsUnreadableException">Thrown when the deployment holds a record for this owner and it could not be handed on — a document past what this build binds, or a database that declined the read. An owner nobody provisioned is the <see langword="null" /> above rather than this.</exception>
    Task<OwnerSettingsDocument?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken);
}
