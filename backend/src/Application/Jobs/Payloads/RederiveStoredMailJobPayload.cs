// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one job at the stored mail of an account, or of one folder of it, and at nothing inside a message.</summary>
/// <remarks>
/// <para>
/// Every property is one of MailFathom's own identifiers: the identifier this deployment generated for the mailbox,
/// and its own name for a folder of it. The document therefore carries
/// nothing derived from a message and has no property one could be put in, and what the work reads about that mail it
/// reads from committed local state.
/// </para>
/// <para>
/// An absent folder is every folder the account holds mail in rather than a folder named by an empty string, which is
/// the same distinction <see cref="Mail.Maintenance.StoredMailScope" /> draws and the reason the
/// property is nullable rather than defaulted.
/// </para>
/// <para>
/// The properties are primitives rather than the domain value objects they came from, because this record is the stored
/// document — one <c>jsonb</c> column an operator reads when they ask what a queued job is. Rebuilding the identities is
/// <see cref="ToAccountId" /> and <see cref="ToFolderAlias" />, which validate them the way the domain types do.
/// </para>
/// </remarks>
public sealed record RederiveStoredMailJobPayload : IJobPayload
{
    /// <summary>Gets the account whose stored mail the work covers.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets MailFathom's own name for the one folder to cover, or <see langword="null" /> for every folder the account holds mail in.</summary>
    public string? FolderAlias { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.RederiveStoredMail;

    /// <summary>Describes one scope of stored mail as the document a job carries.</summary>
    /// <param name="account">The account whose stored mail the work covers, named by its generated identifier.</param>
    /// <param name="folderAlias">The one folder of it to cover, or <see langword="null" /> for every folder.</param>
    /// <returns>The payload naming that scope.</returns>
    public static RederiveStoredMailJobPayload For(MailAccountId account, MailFolderAlias? folderAlias) => new()
    {
        AccountId = account.Value,
        FolderAlias = folderAlias?.Value,
    };

    /// <summary>Rebuilds the generated account identifier this payload names.</summary>
    /// <returns>The account's generated identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value no longer names a valid account.</exception>
    /// <remarks>
    /// The identifier is a required property, so a document that carries none is refused by the deserializer before
    /// this is reached. What remains here is a value that is present and does not name an account, which this refuses
    /// for the reason every payload record refuses a component that no longer validates.
    /// </remarks>
    public MailAccountId ToAccountId() =>
        MailAccountId.Create(this.AccountId);

    /// <summary>Rebuilds the folder alias this payload names, which is absent for a whole-account scope.</summary>
    /// <returns>The folder alias, or <see langword="null" /> when the payload names every folder.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored value no longer names a valid folder alias.</exception>
    public MailFolderAlias? ToFolderAlias() =>
        this.FolderAlias is { Length: > 0 } alias ? MailFolderAlias.Create(alias) : null;
}
