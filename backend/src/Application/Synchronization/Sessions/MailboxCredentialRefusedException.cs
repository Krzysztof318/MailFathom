// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Synchronization.Sessions;

/// <summary>Indicates that a mail server refused the credential MailFathom holds for an account.</summary>
/// <remarks>
/// <para>
/// It is the opposite failure from <see cref="MailboxUnavailableException" /> and the distinction is what a worker
/// acts on. An unavailable server is expected to serve the same work on a later run, so the account is backed off and
/// approached again; a refused credential will be refused identically every run until a person replaces it, so the run
/// is what reports it rather than what retries it.
/// </para>
/// <para>
/// It exists as an application-owned failure so that distinction survives the adapter boundary. A mail library reports
/// a refusal through its own exception types, and a worker reading those directly would be a worker that depends on
/// which library opened the connection.
/// </para>
/// <para>
/// The message names the account alias and nothing else: not the user name, not the mechanism the server selected, not
/// the server's own answer, none of which can be logged.
/// </para>
/// </remarks>
public sealed class MailboxCredentialRefusedException : MailFathomException
{
    /// <summary>Initializes a new credential refusal naming the account it stopped.</summary>
    /// <param name="accountId">The account whose credential the mail server refused.</param>
    /// <param name="innerException">The refusal the mail library reported.</param>
    public MailboxCredentialRefusedException(MailAccountId accountId, Exception innerException)
        : base(
            $"The mail server refused the credential held for {accountId.Value}, so the account cannot be synchronized until it is replaced.",
            innerException) =>
        this.AccountId = accountId;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxCredentialRefused;

    /// <summary>Gets the account whose credential was refused.</summary>
    public MailAccountId AccountId { get; }
}
