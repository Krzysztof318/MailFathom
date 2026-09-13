// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Names one bounded pass over the mail of a held account's erased folders.</summary>
/// <remarks>
/// A pass erases the mail of every erased folder of the account rather than only of the folder named, so a pass owed
/// by an earlier erasure is finished by whichever pass runs next. The folder is part of the identity only so that a
/// second erasure of the same account is a job of its own rather than a duplicate of a pass that already ran.
/// </remarks>
public sealed record EraseLocalMailFolderMailJobPayload : IJobPayload
{
    /// <summary>Gets the user holding the account.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Gets the account's identifier within that user.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the folder whose erasure asked for the work.</summary>
    public required Guid FolderId { get; init; }

    /// <summary>Gets how many passes preceded this one for the same erasure.</summary>
    public int Pass { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.EraseLocalMailFolderMail;

    /// <summary>Gets the account, named by its user and its identifier.</summary>
    [JsonIgnore]
    public MailAccountIdentity Account => MailAccountIdentity.Create(MailUserId.Create(this.UserId), MailAccountId.Create(this.AccountId));

    /// <summary>States the first pass an erasure owes.</summary>
    /// <param name="account">The account.</param>
    /// <param name="folder">The folder that was erased.</param>
    /// <returns>The payload.</returns>
    public static EraseLocalMailFolderMailJobPayload For(MailAccountIdentity account, LocalMailFolderId folder) => new()
    {
        UserId = account.User.Value,
        AccountId = account.Id.Value,
        FolderId = folder.Value,
    };

    /// <summary>States the pass after this one.</summary>
    /// <returns>The payload.</returns>
    public EraseLocalMailFolderMailJobPayload Next() => this with { Pass = this.Pass + 1 };

    /// <summary>Composes the identity that makes one pass run once.</summary>
    /// <returns>The key.</returns>
    public JobIdempotencyKey ToIdempotencyKey() =>
        JobIdempotencyKey.Create($"{JobType.EraseLocalMailFolderMail.Name}:{this.UserId}:{this.AccountId}:{this.FolderId}:{this.Pass}");
}
