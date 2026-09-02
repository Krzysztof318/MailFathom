// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Gives <see cref="OwnerAccountErasure" /> the caller it was written for.</summary>
/// <remarks>
/// <para>
/// The seam already knew how to erase an owner and had nothing that invoked it, which is what this supplies: one
/// transaction around the whole walk, so a request answered as erased is true of every table at once rather than of
/// whichever statements had committed when something failed.
/// </para>
/// <para>
/// It runs under the ordinary commit policy rather than a bare session, because the walk takes a row lock on the owner
/// and a writer holding one of the rows it deletes can lose the race — a synchronization run that inserted an account
/// against this owner while the transaction was open is the case. A replay stages the erasure again from a fresh read
/// and reaches the same end state, which is what makes repeating it safe.
/// </para>
/// <para>
/// What the count of rows the cascade did not reach says is left where it is measured. A caller asking to erase an
/// owner is asking whether the deployment still holds them, and how many rows of which shape it took to answer that is
/// the seam's own accounting rather than an operator's.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this eraser.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedMailOwnerErasure(OptimisticConcurrencyRetryPolicy commitPolicy) : IMailOwnerErasure
{
    /// <inheritdoc />
    public async Task<bool> EraseAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An owner record is erased for a named owner.", nameof(owner));
        }

        var erasure = await commitPolicy.CommitAsync(
            (session, token) => OwnerAccountErasure.EraseAsync(session, owner.Value, token),
            cancellationToken);

        return erasure.OwnerErased;
    }
}
