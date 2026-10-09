// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>Answers questions about every mail account this deployment serves, from the account records themselves.</summary>
/// <remarks>
/// <para>
/// An account is served when it holds an address, its document binds, and somebody is assigned it. Each of the three
/// is what the composed roster required of an account before it published one: an account the upgrade derived no
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
}
