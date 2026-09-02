// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The owner an administrator asks a deployment to record.</summary>
/// <param name="DisplayName">The label the owner is told apart by, unique across the deployment.</param>
/// <remarks>The identifier is not here: the deployment mints one, so a command supplying one would decide an identity it does not own.</remarks>
internal sealed record OwnerProvisioningRequest(
    [property: JsonPropertyName("displayName")] string DisplayName);

/// <summary>The label an owner is told apart by from now on.</summary>
/// <param name="DisplayName">What an administrator selects this owner by, unique across the deployment.</param>
/// <remarks>Its own type rather than the provisioning request, because the deployment reads the two on different routes and a shared shape would let a rename be composed as a recording.</remarks>
internal sealed record OwnerRelabelRequest(
    [property: JsonPropertyName("displayName")] string DisplayName);

/// <summary>The owner a provisioning recorded.</summary>
/// <param name="Id">The identifier the deployment minted.</param>
internal sealed record OwnerProvisioned([property: JsonPropertyName("id")] Guid Id);

/// <summary>What an erasure removed.</summary>
/// <param name="Erased">Whether the deployment held the owner at all.</param>
/// <param name="WasServed">Whether the running deployment was serving them when they were erased.</param>
/// <remarks>The second reports whether the erasure also removed the owner from the running process.</remarks>
internal sealed record OwnerErasure(
    [property: JsonPropertyName("erased")] bool Erased,
    [property: JsonPropertyName("wasServed")] bool WasServed);

/// <summary>One owner's record as the deployment holds it.</summary>
/// <param name="Owner">The owner the record belongs to.</param>
/// <param name="DisplayName">The label the owner is recorded under.</param>
/// <param name="Version">The version the record was read at, which the next write is composed over.</param>
/// <param name="Source">Where this owner's mail accounts are read from today.</param>
/// <param name="ReadFromConfiguration">Whether a configuration source still supplies them, which is what makes every write but an adoption refused.</param>
/// <param name="Document">The record, with every secret-bearing value replaced by the deployment's redaction marker.</param>
internal sealed record OwnerRecord(
    [property: JsonPropertyName("owner")] Guid Owner,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("readFromConfiguration")] bool ReadFromConfiguration,
    [property: JsonPropertyName("document")] string? Document);

/// <summary>One mail account declared into an owner's record.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="Account">The declaration, as the JSON object a configuration file would have written.</param>
internal sealed record OwnerMailAccountRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("account")] string Account);

/// <summary>The mail account an owner's record stops declaring.</summary>
/// <param name="Version">The version the record was read at.</param>
/// <param name="AccountId">The identifier the account was declared under.</param>
internal sealed record OwnerMailAccountRemovalRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("accountId")] string AccountId);

/// <summary>The version an adoption was previewed over.</summary>
/// <param name="Version">The version the preview reported.</param>
internal sealed record OwnerAdoptionRequest([property: JsonPropertyName("version")] long Version);

/// <summary>What adopting one owner would move into their record.</summary>
/// <param name="Owner">The owner asked about.</param>
/// <param name="DisplayName">The label the owner is recorded under.</param>
/// <param name="Version">The version the record stands at, which the adoption is accepted against.</param>
/// <param name="Source">Where this owner's mail accounts are read from today.</param>
/// <param name="ReadFromConfiguration">Whether a configuration source still supplies them, which is whether there is an adoption to perform at all.</param>
/// <param name="ConfigurationPath">The configuration path that stops deciding them once the adoption commits, and nothing where no source supplies them.</param>
/// <param name="MailAccounts">The mail accounts the adoption would move, empty where the source supplies none.</param>
/// <param name="Classification">The classification posture the adoption would commit beside them, empty where the deployment states none.</param>
/// <param name="SensitiveContent">The scanning block the adoption would commit beside them, empty where their declaration states none.</param>
internal sealed record OwnerAdoptionPreview(
    [property: JsonPropertyName("owner")] Guid Owner,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("readFromConfiguration")] bool ReadFromConfiguration,
    [property: JsonPropertyName("configurationPath")] string? ConfigurationPath,
    [property: JsonPropertyName("mailAccounts")] IReadOnlyList<OwnerAdoptableMailAccount>? MailAccounts,
    [property: JsonPropertyName("classification")] IReadOnlyList<OwnerAdoptableRecordSetting>? Classification,
    [property: JsonPropertyName("sensitiveContent")] IReadOnlyList<OwnerAdoptableRecordSetting>? SensitiveContent);

/// <summary>One mail account an adoption would move into an owner's record.</summary>
/// <param name="AccountId">The identifier the account is declared under.</param>
/// <param name="DisplayName">The name the account is published under.</param>
internal sealed record OwnerAdoptableMailAccount(
    [property: JsonPropertyName("accountId")] string? AccountId,
    [property: JsonPropertyName("displayName")] string? DisplayName);

/// <summary>One classification setting an adoption would commit into an owner's record.</summary>
/// <param name="Path">The path the setting is written at in the record.</param>
/// <param name="Value">The value it takes, which is what the deployment's section states today.</param>
internal sealed record OwnerAdoptableRecordSetting(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("value")] string? Value);

/// <summary>What one write to an owner's record produced.</summary>
/// <param name="Committed">Whether the record moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and nothing where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and empty on a commit.</param>
/// <remarks>A refusal arrives as a named outcome with a success status for the reason a configuration write's does: each one is something the operator acts on and continues from, and each carries the version the next attempt is composed over.</remarks>
internal sealed record OwnerRecordWriteAnswer(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("messages")] IReadOnlyList<string>? Messages)
{
    /// <summary>The deployment's code for a write to an owner a configuration source still supplies.</summary>
    /// <remarks>Named here because a command acts on it rather than only reporting it: it is the one refusal whose repair is another command of this tool.</remarks>
    internal const int RecordReadFromConfiguration = 12015;

    /// <summary>States what the deployment said about a write that did not commit.</summary>
    /// <returns>One sentence per reason, or a single sentence where the deployment gave none.</returns>
    internal IReadOnlyList<string> DescribeRefusal() => this.Messages is { Count: > 0 } stated
        ? stated
        : ["The deployment did not commit the write and said nothing this command could act on."];
}
