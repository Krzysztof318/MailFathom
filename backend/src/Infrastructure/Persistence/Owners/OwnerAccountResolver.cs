// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Answers which owner a mailbox this deployment was configured with belongs to.</summary>
/// <remarks>
/// <para>
/// The owner record is authoritative and this reads it. Binding a folder is what first writes an account row, and the
/// row needs an owner the moment it exists — so the alternative to reading one would be minting one there, which would
/// let a synchronization run create the security boundary its own mail is then judged against.
/// </para>
/// <para>
/// A deployment carries exactly one owner while its mail accounts are declared in configuration, because a configured
/// account names no owner and nothing else could decide which of several it meant. The upgrade provisions that one
/// row, so the answer is present before the first run reaches this; zero and several are both refused rather than
/// resolved, and each says which of the two it was.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class OwnerAccountResolver
{
    /// <summary>Reads the owner a configured mail account belongs to.</summary>
    /// <param name="dbContext">The context the read runs on, which is the caller's transaction where it has one.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identity of the deployment's owner.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the deployment holds no owner record, or holds more than one while a configured account still has
    /// to be attributed to one of them.
    /// </exception>
    public static async Task<Guid> ResolveConfiguredOwnerAsync(
        MailFathomDbContext dbContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        // Two rows are read rather than one, so "several owners" is reported as itself instead of arriving as whichever
        // row the database happened to return first.
        var owners = await dbContext.OwnerAccounts
            .AsNoTracking()
            .OrderBy(owner => owner.CreatedAt)
            .ThenBy(owner => owner.Id)
            .Select(owner => owner.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);

        return owners switch
        {
            [var soleOwner] => soleOwner,
            [] => throw new InvalidOperationException(
                "This deployment holds no owner record, so a configured mail account belongs to nobody. Apply the "
                + "schema of this release, which provisions the owner an upgraded deployment's accounts are carried onto."),
            _ => throw new InvalidOperationException(
                "This deployment holds more than one owner record, so a mail account declared in configuration cannot "
                + "be attributed to one of them. Declare the account in the owner record that is to own it."),
        };
    }
}
