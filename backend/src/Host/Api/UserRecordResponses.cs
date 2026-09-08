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
/// <param name="RecordIsTheirOwn">Whether this user's mail accounts come from their own record rather than from a configuration source.</param>
/// <param name="Served">Whether the running process is serving them, which is <see langword="false" /> for one no source declares.</param>
/// <param name="DeclaredInConfiguration">Whether a configuration source names this user, so a start puts their label back and writes their row again after an erasure.</param>
/// <remarks>
/// The last three are reported apart because they answer different questions and an operator acts on each differently.
/// A user whose record is not yet their own is one an adoption still has something to move; a user the process is
/// not serving is one whose mail is neither read nor refreshed; a user a configuration source declares is one whose
/// label a start rewrites and whose erasure a start undoes.
/// </remarks>
internal sealed record UserRosterEntryResponse(
    Guid Id,
    string DisplayName,
    bool RecordIsTheirOwn,
    bool Served,
    bool DeclaredInConfiguration)
{
    /// <summary>Describes one user.</summary>
    /// <param name="entry">The user as the roster reported them.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static UserRosterEntryResponse For(UserRosterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new UserRosterEntryResponse(
            entry.User.Value,
            entry.DisplayName,
            entry.RecordIsTheirOwn,
            entry.Served,
            entry.DeclaredInConfiguration);
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
/// <param name="Source">The published name of where this user's mail accounts are read from.</param>
/// <param name="ReadFromConfiguration">Whether a configuration source still supplies them, which is what makes every write but an adoption refused.</param>
/// <param name="Document">The record, with every secret-bearing value replaced by the redaction marker.</param>
internal sealed record UserRecordResponse(
    Guid User,
    string DisplayName,
    long Version,
    string Source,
    bool ReadFromConfiguration,
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
            record.Source.ToString(),
            record.ReadFromConfiguration,
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

/// <summary>The version an adoption was previewed over.</summary>
/// <param name="Version">The version the preview reported.</param>
internal sealed record UserAdoptionRequest(long Version);

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

/// <summary>What adopting one user would move into their record.</summary>
/// <param name="User">The user asked about.</param>
/// <param name="DisplayName">The label the user is recorded under.</param>
/// <param name="Version">The version the record stands at, which the adoption is accepted against.</param>
/// <param name="Source">The published name of where this user's mail accounts are read from today.</param>
/// <param name="ReadFromConfiguration">Whether a configuration source still supplies them, which is whether there is an adoption to perform at all.</param>
/// <param name="ConfigurationPath">The configuration path that stops deciding them once the adoption commits, and nothing where no source supplies them.</param>
/// <param name="MailAccounts">The mail accounts the adoption would move, empty where the source supplies none.</param>
/// <param name="Classification">The classification posture the adoption would commit beside them, empty where the deployment states none.</param>
/// <param name="SensitiveContent">The scanning block the adoption would commit beside them, empty where their declaration states none.</param>
/// <remarks>The flag is published beside the source name rather than left to be derived from it, because whether there is anything to adopt is the question a caller acts on and reading it out of a name would make an enumeration member's spelling part of the contract.</remarks>
internal sealed record UserAdoptionPreviewResponse(
    Guid User,
    string DisplayName,
    long Version,
    string Source,
    bool ReadFromConfiguration,
    string? ConfigurationPath,
    IReadOnlyList<UserAdoptableMailAccountResponse> MailAccounts,
    IReadOnlyList<UserAdoptableRecordSettingResponse> Classification,
    IReadOnlyList<UserAdoptableRecordSettingResponse> SensitiveContent)
{
    /// <summary>Describes an adoption preview.</summary>
    /// <param name="preview">The preview as the administration read it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preview" /> is <see langword="null" />.</exception>
    internal static UserAdoptionPreviewResponse For(UserAdoptionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new UserAdoptionPreviewResponse(
            preview.User.Value,
            preview.DisplayName,
            preview.Version,
            preview.Source.ToString(),
            preview.HasSomethingToAdopt,
            preview.ConfigurationPath,
            [.. preview.MailAccounts.Select(UserAdoptableMailAccountResponse.For)],
            [.. preview.Classification.Select(UserAdoptableRecordSettingResponse.For)],
            [.. preview.SensitiveContent.Select(UserAdoptableRecordSettingResponse.For)]);
    }
}

/// <summary>One mail account an adoption would move into a user's record.</summary>
/// <param name="AccountId">The identifier the account is declared under.</param>
/// <param name="DisplayName">The name the account is published under.</param>
/// <remarks>Neither a mail server, a port, a user name, nor anything derived from a credential is here: the preview exists so an operator can confirm which mailboxes are about to move, and the identifier and the label are what they recognize them by.</remarks>
internal sealed record UserAdoptableMailAccountResponse(string AccountId, string DisplayName)
{
    /// <summary>Describes one adoptable account.</summary>
    /// <param name="account">The account as the preview reported it.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal static UserAdoptableMailAccountResponse For(UserAdoptableMailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new UserAdoptableMailAccountResponse(account.AccountId, account.DisplayName);
    }
}

/// <summary>One setting an adoption would commit into a user's record beside their mailboxes.</summary>
/// <param name="Path">The path the setting is written at in the record, rooted at its own block.</param>
/// <param name="Value">The value it takes, which is what the deployment's section states today.</param>
/// <remarks>Nothing under the deployment's scanner block is here, so this reports what would be decided about the user's mail without disclosing where the daemon is or what reaches it.</remarks>
internal sealed record UserAdoptableRecordSettingResponse(string Path, string Value)
{
    /// <summary>Describes one adoptable posture setting.</summary>
    /// <param name="setting">The setting as the preview reported it.</param>
    /// <returns>The response entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="setting" /> is <see langword="null" />.</exception>
    internal static UserAdoptableRecordSettingResponse For(UserAdoptableRecordSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return new UserAdoptableRecordSettingResponse(setting.Path, setting.Value);
    }
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
