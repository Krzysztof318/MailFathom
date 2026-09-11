// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.UserSettings.Administration;

namespace MailFathom.Host.Api;

/// <summary>What the deployment reports when asked which users it holds.</summary>
/// <param name="Users">One entry per user, in the order the deployment recorded them.</param>
internal sealed record UserRosterResponse(IReadOnlyList<UserRosterEntryResponse> Users)
{
    /// <summary>Describes a roster reading.</summary>
    /// <param name="roster">The users the deployment holds.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="roster" /> is <see langword="null" />.</exception>
    internal static UserRosterResponse For(IReadOnlyList<UserRosterEntry> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);

        return new UserRosterResponse([.. roster.Select(UserRosterEntryResponse.For)]);
    }
}

/// <summary>One user this deployment holds.</summary>
/// <param name="Id">The identifier the user was minted under, which every other act names them by.</param>
/// <param name="DisplayName">The label an administrator tells them apart by, which may change and is never the identity.</param>
/// <param name="Served">Whether the running process is serving them, which every user it holds is; a user it is not serving is one whose mail is neither read nor refreshed.</param>
internal sealed record UserRosterEntryResponse(
    Guid Id,
    string DisplayName,
    bool Served)
{
    /// <summary>Describes one user.</summary>
    /// <param name="entry">The user as the roster reported them.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static UserRosterEntryResponse For(UserRosterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new UserRosterEntryResponse(entry.User.Value, entry.DisplayName, entry.Served);
    }
}

/// <summary>The label a user is recorded under.</summary>
/// <param name="DisplayName">What an administrator tells this user apart by, unique across the deployment.</param>
/// <remarks>The identifier is not here and never is: this deployment mints one, so a caller supplying one would decide an identity it does not own.</remarks>
internal sealed record UserProvisioningRequest(string? DisplayName);

/// <summary>The label a user is relabelled to.</summary>
/// <param name="DisplayName">What an administrator tells this user apart by from now on, unique across the deployment.</param>
/// <remarks>Its own request type rather than the provisioning one, because the two carry the same field for different acts: a body that named a user would be a rename that recorded somebody, and a shared type is what would let one become the other.</remarks>
internal sealed record UserRelabelRequest(string? DisplayName);

/// <summary>The user a provisioning recorded.</summary>
/// <param name="Id">The identifier the user was minted under.</param>
internal sealed record UserProvisionedResponse(Guid Id);

/// <summary>What an erasure removed.</summary>
/// <param name="Erased">Whether this deployment held the user at all.</param>
/// <param name="WasServed">Whether the running process was serving them when they were erased.</param>
/// <remarks>The second reports whether the erasure also removed the user from the running process.</remarks>
internal sealed record UserErasureResponse(bool Erased, bool WasServed);

/// <summary>What the deployment reports when asked for one user's record.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="DisplayName">The label the user is recorded under.</param>
/// <param name="Version">The version the record was read at, which the commit that follows is accepted against.</param>
/// <param name="Document">The record, with every secret-bearing value replaced by the redaction marker.</param>
internal sealed record UserRecordResponse(
    Guid User,
    string DisplayName,
    long Version,
    string Document)
{
    /// <summary>Describes a record reading.</summary>
    /// <param name="record">The record as the administration read it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    internal static UserRecordResponse For(UserRecordReading record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new UserRecordResponse(
            record.User.Value,
            record.DisplayName,
            record.Version,
            record.Json);
    }
}

/// <summary>The whole record an editing session saved.</summary>
/// <param name="Version">The version the buffer was opened over.</param>
/// <param name="Document">The record as the operator saved it.</param>
internal sealed record UserRecordSaveRequest(long Version, string? Document);

/// <summary>One mail account declared into a user's record.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="Account">The declaration, as the JSON object a configuration file would have written.</param>
/// <remarks>The settings travel as the document a file states them in rather than as a typed body, so what an operator writes for an account of their own is what they would have written for one of the deployment's — and so a setting added to that shape needs nothing added here.</remarks>
internal sealed record UserMailAccountRequest(long Version, string? Account);

/// <summary>The mail account a user's record stops declaring.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="AccountId">The identifier the account was declared under.</param>
internal sealed record UserMailAccountRemovalRequest(long Version, string? AccountId);

/// <summary>One folder declared into a mail account of the acting user's record.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="AccountId">The identifier the account the folder belongs to was declared under.</param>
/// <param name="Folder">The declaration, as the JSON object a configuration file would have written.</param>
/// <remarks>The folder travels as the document a file states it in for the reason a mail account does: it is the same shape, judged by the same binder, so a setting a folder gains needs nothing added here.</remarks>
internal sealed record UserFolderRequest(long Version, string? AccountId, string? Folder);

/// <summary>One folder of the acting user's record stated afresh, in place of the one carrying an alias.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="AccountId">The identifier the account the folder belongs to was declared under.</param>
/// <param name="Alias">The alias the folder being changed is declared under, which the declaration itself may move away from.</param>
/// <param name="Folder">The folder as it is to stand, as the JSON object a configuration file would have written.</param>
/// <remarks>The alias is carried beside the declaration rather than read out of it, because renaming a folder is exactly the change where the two differ: what is being replaced is found by the name it has now, and what replaces it carries the name it is taking.</remarks>
internal sealed record UserFolderReplacementRequest(long Version, string? AccountId, string? Alias, string? Folder);

/// <summary>The folder a mail account of the acting user's record stops declaring.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="AccountId">The identifier the account the folder belongs to was declared under.</param>
/// <param name="Alias">The alias the folder was declared under.</param>
internal sealed record UserFolderRemovalRequest(long Version, string? AccountId, string? Alias);

/// <summary>Material an administrator asks this deployment to store for one user.</summary>
/// <param name="Name">The stable declared name used for rotation and audit.</param>
/// <param name="Material">The material to seal, carried only in this request.</param>
/// <remarks><see cref="ToString" /> reports no field, so rendering the request cannot disclose material.</remarks>
internal sealed record StoredSecretWriteRequest(string? Name, string? Material)
{
    /// <inheritdoc />
    public override string ToString() => nameof(StoredSecretWriteRequest);
}

/// <summary>The reference a successful stored-secret write produced.</summary>
/// <param name="SecretReference">The value a user document keeps instead of material.</param>
/// <remarks><see cref="ToString" /> reports no field, so a diagnostic cannot print the reference target by rendering the response.</remarks>
internal sealed record StoredSecretProvisionedResponse(string SecretReference)
{
    /// <inheritdoc />
    public override string ToString() => nameof(StoredSecretProvisionedResponse);
}

/// <summary>What one write to a user's record did.</summary>
/// <param name="Committed">Whether the record moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and <see langword="null" /> where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and empty on a commit.</param>
/// <remarks>
/// A refusal arrives as an outcome with a success status rather than as an error, for the reason a configuration
/// write's does: every one of them is something the caller acts on and continues from — a record somebody else moved
/// on, a declaration that will not bind, a user a file still supplies — and each carries the version they compose the
/// next attempt over. No message carries a secret, a mail server, or a user name; a refusal about a credential names
/// the setting rather than the value.
/// </remarks>
internal sealed record UserRecordWriteResponse(
    bool Committed,
    long Version,
    int? Code,
    IReadOnlyList<string> Messages)
{
    /// <summary>Describes what a write did.</summary>
    /// <param name="outcome">The outcome the administration reported.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    internal static UserRecordWriteResponse For(UserRecordWriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new UserRecordWriteResponse(
            outcome.IsCommitted,
            outcome.Version,
            outcome.Refusal.IsSpecified ? outcome.Refusal.Value : null,
            outcome.Messages);
    }
}
