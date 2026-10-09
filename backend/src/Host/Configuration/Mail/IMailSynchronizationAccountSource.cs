// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Reads which mail accounts synchronization supervises, and what one account's run reads, from the account records.</summary>
/// <remarks>
/// <para>
/// The coordinator and every supervisor it starts read here rather than off a set of users every replica composes, so what a
/// pass costs follows the accounts the database holds and what a run costs follows the handful of accounts it touches.
/// </para>
/// <para>
/// An interface because the coordinator and the supervisor are tested over accounts a test states rather than over
/// rows, and the persisted implementation is one statement per page and a few per run that only the integration suite
/// can reach.
/// </para>
/// </remarks>
internal interface IMailSynchronizationAccountSource
{
    /// <summary>Reads every account synchronization may supervise, page by page, each with the version its record stands at.</summary>
    /// <param name="cancellationToken">Cancels the read between and within pages.</param>
    /// <returns>The accounts, in the order of their identifiers; an account an erasure is deciding about is left out.</returns>
    IAsyncEnumerable<SupervisedMailAccount> ReadSupervisedAsync(CancellationToken cancellationToken);

    /// <summary>Reads the settings one account's work unit runs against.</summary>
    /// <param name="account">The account the work unit runs for.</param>
    /// <param name="previous">What the account's previous run read, or <see langword="null" /> for its first run.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The deployment's bound settings carrying the account and the other accounts of every user it is assigned to, or
    /// <see langword="null" /> when the account is no longer one synchronization supervises. It is
    /// <paramref name="previous" /> itself when nothing that was composed from has changed since.
    /// </returns>
    /// <remarks>
    /// Answering with the same instance is what keeps a push session open across runs: a watch pinned to the settings it
    /// connected under recycles its session only once they are replaced, which is the moment a rotated credential or a
    /// changed endpoint has to reach it.
    /// </remarks>
    Task<MailSynchronizationOptions?> ReadRunSettingsAsync(
        MailAccountId account,
        MailSynchronizationOptions? previous,
        CancellationToken cancellationToken);

    /// <summary>Reads the settings a work unit acting for one user runs against.</summary>
    /// <param name="user">The user the work unit acts for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The deployment's bound settings carrying every account of that user, or none where the user is not served.</returns>
    /// <remarks>
    /// Read through the served-user cache rather than per call, because a request reaches here on every one a person
    /// makes: what a request costs is a lookup while their record has not moved.
    /// </remarks>
    Task<MailSynchronizationOptions> ReadUserSettingsAsync(UserId user, CancellationToken cancellationToken);

    /// <summary>Gets a token that changes once the accounts <see cref="ReadSupervisedAsync" /> reads may have changed.</summary>
    /// <returns>The token for the next change, which a reader asks for again after every one.</returns>
    IChangeToken GetChangeToken();
}
