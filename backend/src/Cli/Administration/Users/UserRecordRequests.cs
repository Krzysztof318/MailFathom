// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Users;

/// <summary>The user an administrator asks a deployment to record.</summary>
/// <param name="DisplayName">The label the user is told apart by, unique across the deployment.</param>
/// <remarks>The identifier is not here: the deployment mints one, so a command supplying one would decide an identity it does not own.</remarks>
internal sealed record UserProvisioningRequest(
    [property: JsonPropertyName("displayName")] string DisplayName);

/// <summary>The label a user is told apart by from now on.</summary>
/// <param name="DisplayName">What an administrator selects this user by, unique across the deployment.</param>
/// <remarks>Its own type rather than the provisioning request, because the deployment reads the two on different routes and a shared shape would let a rename be composed as a recording.</remarks>
internal sealed record UserRelabelRequest(
    [property: JsonPropertyName("displayName")] string DisplayName);

/// <summary>The user a provisioning recorded.</summary>
/// <param name="Id">The identifier the deployment minted.</param>
internal sealed record UserProvisioned([property: JsonPropertyName("id")] Guid Id);

/// <summary>What an erasure removed.</summary>
/// <param name="Erased">Whether the deployment held the user at all.</param>
/// <param name="WasServed">Whether the running deployment was serving them when they were erased.</param>
/// <remarks>The second reports whether the erasure also removed the user from the running process.</remarks>
internal sealed record UserErasure(
    [property: JsonPropertyName("erased")] bool Erased,
    [property: JsonPropertyName("wasServed")] bool WasServed);

/// <summary>One user's record as the deployment holds it.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="DisplayName">The label the user is recorded under.</param>
/// <param name="Version">The version the record was read at, which the next write is composed over.</param>
/// <param name="ReadFromConfiguration">Whether a configuration source still supplies this user's mail accounts, which is what would make every write into their record refused.</param>
/// <param name="Document">The record, with every secret-bearing value replaced by the deployment's redaction marker.</param>
internal sealed record UserRecord(
    [property: JsonPropertyName("user")] Guid User,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("readFromConfiguration")] bool ReadFromConfiguration,
    [property: JsonPropertyName("document")] string? Document);

/// <summary>One user's record as an editing session saved it.</summary>
/// <param name="Version">The version the buffer was opened over, which the commit is accepted against.</param>
/// <param name="Document">The record as the operator left it, which the deployment judges whole.</param>
/// <remarks>
/// The whole record rather than the difference, for the reason the contact amendment sends a whole record: what the
/// deployment accepts is a document, and a difference would have to be applied by something that then decided what the
/// record means. The version is what makes that safe — a buffer composed over a record somebody else has moved past is
/// refused rather than merged.
/// </remarks>
internal sealed record UserRecordSaveRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("document")] string Document);

/// <summary>One mail account declared into a user's record.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="Account">The declaration, as the JSON object a configuration file would have written.</param>
internal sealed record UserMailAccountRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("account")] string Account);

/// <summary>The mail account a user's record stops declaring.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="AccountId">The identifier the account was declared under.</param>
internal sealed record UserMailAccountRemovalRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("accountId")] string AccountId);

/// <summary>What one write to a user's record produced.</summary>
/// <param name="Committed">Whether the record moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and nothing where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and empty on a commit.</param>
/// <remarks>A refusal arrives as a named outcome with a success status for the reason a configuration write's does: each one is something the operator acts on and continues from, and each carries the version the next attempt is composed over.</remarks>
internal sealed record UserRecordWriteAnswer(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("messages")] IReadOnlyList<string>? Messages)
{
    /// <summary>The deployment's code for a write to a user a configuration source still supplies.</summary>
    /// <remarks>Named here because a command acts on it rather than only reporting it: it is the one refusal whose repair is another command of this tool.</remarks>
    internal const int RecordReadFromConfiguration = 12015;

    /// <summary>States what the deployment said about a write that did not commit.</summary>
    /// <returns>One sentence per reason, or a single sentence where the deployment gave none.</returns>
    internal IReadOnlyList<string> DescribeRefusal() => this.Messages is { Count: > 0 } stated
        ? stated
        : ["The deployment did not commit the write and said nothing this command could act on."];
}
