// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Names one bounded pass over the stored mail of a folder an account no longer declares.</summary>
/// <remarks>
/// The alias is part of the identity as well as of the work, because a deletion of a second folder of the same account
/// is a job of its own rather than a duplicate of a pass that is already running for the first. That is the opposite of
/// what a held account's erasure needs, where one pass reaches every erased folder at once: a withdrawn folder's mail
/// is selected by its alias, so each folder is swept on its own.
/// </remarks>
public sealed record EraseWithdrawnMailFolderMailJobPayload : IJobPayload
{
    /// <summary>Gets the user the account is served to.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the account's identifier within that user.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets MailFathom's own name for the folder whose deletion asked for the work.</summary>
    public required string FolderAlias { get; init; }

    /// <summary>Gets how many passes preceded this one for the same deletion.</summary>
    public int Pass { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.EraseWithdrawnMailFolderMail;

    /// <summary>Gets the account, named by its user and its identifier.</summary>
    [JsonIgnore]
    public MailAccountIdentity Account =>
        MailAccountIdentity.Create(MailUserId.Create(this.UserId), MailAccountId.Create(this.AccountId));

    /// <summary>Gets the folder the pass sweeps.</summary>
    [JsonIgnore]
    public MailFolderAlias Folder => MailFolderAlias.Create(this.FolderAlias);

    /// <summary>States the first pass a deletion owes.</summary>
    /// <param name="account">The account.</param>
    /// <param name="folderAlias">The folder whose declaration was withdrawn.</param>
    /// <returns>The payload.</returns>
    public static EraseWithdrawnMailFolderMailJobPayload For(MailAccountIdentity account, MailFolderAlias folderAlias) =>
        new()
        {
            UserId = account.User.Value,
            AccountId = account.Id.Value,
            FolderAlias = folderAlias.Value,
        };

    /// <summary>States the pass after this one.</summary>
    /// <returns>The payload.</returns>
    public EraseWithdrawnMailFolderMailJobPayload Next() => this with { Pass = this.Pass + 1 };

    /// <summary>Composes the identity that makes one pass run once.</summary>
    /// <returns>The key.</returns>
    public JobIdempotencyKey ToIdempotencyKey() => JobIdempotencyKey.Create(
        $"{JobType.EraseWithdrawnMailFolderMail.Name}:{this.UserId}:{this.AccountId}:{this.FolderAlias}:{this.Pass}");
}
