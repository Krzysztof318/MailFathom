// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>Answers questions about every mail account this deployment serves, from the account records themselves.</summary>
/// <remarks>
/// <para>
/// An account is served when it holds an address, its document binds, and somebody is assigned it. Each of the three
/// is what composing a user requires of an account before serving it: an account the upgrade derived no
/// address for waits for one to be stated, a document nothing can bind declares nothing, and an account nobody is
/// assigned is read by nobody.
/// </para>
/// <para>
/// Nothing here is held between calls. Every answer is one query, so a caller that needs one several times reads it
/// once at the start of its work and keeps the value for as long as that work runs.
/// </para>
/// </remarks>
public interface IServedMailAccountReader
{
    /// <summary>Reads every account this deployment serves, by the columns that name it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The served accounts, in the ordinal order of their identifiers.</returns>
    Task<IReadOnlyList<ServedMailAccountRow>> ReadServedAsync(CancellationToken cancellationToken);

    /// <summary>Reads which of the named accounts this deployment serves, by the columns that name them.</summary>
    /// <param name="among">The accounts asked about; one this deployment does not serve is left out of the answer.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The served accounts among those named, in the ordinal order of their identifiers.</returns>
    Task<IReadOnlyList<ServedMailAccountRow>> ReadServedAsync(
        IReadOnlyCollection<Guid> among,
        CancellationToken cancellationToken);

    /// <summary>Reads one page of the accounts this deployment serves, each with the version its record stands at.</summary>
    /// <param name="after">The last identifier of the previous page, or <see langword="null" /> for the first page.</param>
    /// <param name="limit">The most accounts the page holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, in the order of the identifiers; a page shorter than <paramref name="limit" /> is the last.</returns>
    /// <remarks>
    /// Keyed on the identifier rather than on a position, so an account recorded or erased between two pages moves no
    /// other account across the boundary: a walk of every page reads every account that was served throughout it once.
    /// </remarks>
    Task<IReadOnlyList<ServedMailAccountVersion>> ReadServedVersionsAsync(
        Guid? after,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Reads every account this deployment serves whole, document included.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The served accounts, in the ordinal order of their identifiers.</returns>
    Task<IReadOnlyList<MailAccountRecord>> ReadServedRecordsAsync(CancellationToken cancellationToken);

    /// <summary>Reads accounts whose settings columns were not read out of the document they now hold, served or not.</summary>
    /// <param name="limit">The most accounts to read, in identifier order.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts, each at the version its document stands at, without the document where it is past the size MailFathom binds one from.</returns>
    /// <remarks>
    /// Only a build older than those columns leaves one, by rewriting or creating the document alone during a rolling
    /// upgrade, and the migration that added them leaves every row it filled for the host's own binding to read again.
    /// </remarks>
    Task<IReadOnlyList<MailAccountTrailingSettings>> ReadTrailingSettingsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Reads each distinct answer served accounts gave about scanning their mail.</summary>
    /// <param name="user">The user whose assigned accounts are asked about, or <see langword="null" /> for every served account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per distinct answer, which is a handful however many accounts gave it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    Task<IReadOnlyList<MailAccountScanningRequest>> ReadScanningRequestsAsync(
        UserId? user,
        CancellationToken cancellationToken);

    /// <summary>Reads what one served account asked for about scanning its mail.</summary>
    /// <param name="account">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account's answer, or <see langword="null" /> where this deployment does not serve it.</returns>
    Task<MailAccountScanningRequest?> ReadScanningRequestAsync(
        MailAccountId account,
        CancellationToken cancellationToken);

    /// <summary>Reads every served account that asked for a scanner or a screening of its own.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Those accounts with what each asked for, in identifier order; an account that asked for nothing is left out.</returns>
    /// <remarks>
    /// An account that asked for nothing is scanned under the deployment's own section, so what a caller judging every
    /// account at once needs named is only the accounts whose own record could differ from it.
    /// </remarks>
    Task<IReadOnlyList<MailAccountScanningDeclaration>> ReadAccountsRequestingScanningAsync(
        CancellationToken cancellationToken);
}
