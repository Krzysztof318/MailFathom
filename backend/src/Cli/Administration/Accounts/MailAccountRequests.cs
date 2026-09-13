// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Accounts;

/// <summary>The mail accounts a deployment holds.</summary>
/// <param name="Accounts">One entry per account, in the order they were created in.</param>
internal sealed record MailAccountList(
    [property: JsonPropertyName("accounts")] IReadOnlyList<MailAccountEntry>? Accounts);

/// <summary>One mail account as the deployment holds it.</summary>
/// <param name="Id">The identifier the deployment generated for the account.</param>
/// <param name="Version">The version the account was read at, which the next save is composed over.</param>
/// <param name="Users">The users the account is assigned to.</param>
/// <param name="Declaration">The address, the display name, and the settings, with every secret-bearing value replaced by the deployment's redaction marker.</param>
internal sealed record MailAccountEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("users")] IReadOnlyList<Guid>? Users,
    [property: JsonPropertyName("declaration")] string? Declaration);

/// <summary>A mail account created for one user.</summary>
/// <param name="UserId">The user the account is created for.</param>
/// <param name="Account">The declaration, as one JSON object.</param>
internal sealed record MailAccountCreationRequest(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("account")] string Account);

/// <summary>A mail account's declaration as an editing session saved it.</summary>
/// <param name="Version">The version the buffer was opened over.</param>
/// <param name="Account">The declaration as the operator left it.</param>
internal sealed record MailAccountSaveRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("account")] string Account);

/// <summary>The user an assignment is made to or ended for.</summary>
/// <param name="UserId">The user.</param>
internal sealed record MailAccountAssignmentRequest(
    [property: JsonPropertyName("userId")] Guid UserId);

/// <summary>What one write to a mail account produced.</summary>
/// <param name="Committed">Whether the write moved anything.</param>
/// <param name="Version">The version now in force.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and nothing where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and on a commit one per problem the account already carried.</param>
/// <param name="AccountId">The identifier a created account was generated under.</param>
internal sealed record MailAccountWriteAnswer(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("messages")] IReadOnlyList<string>? Messages,
    [property: JsonPropertyName("accountId")] Guid? AccountId)
{
    /// <summary>States what the deployment said about a write that did not commit.</summary>
    /// <returns>One sentence per reason, or a single sentence where the deployment gave none.</returns>
    internal IReadOnlyList<string> DescribeRefusal() => this.Messages is { Count: > 0 } stated
        ? stated
        : ["The deployment did not commit the write and said nothing this command could act on."];
}

/// <summary>What ending an assignment did.</summary>
/// <param name="Unassigned">Whether the user was assigned the account at all.</param>
/// <param name="AccountErased">Whether the account and its mail were erased because nobody else is assigned it.</param>
internal sealed record MailAccountUnassignment(
    [property: JsonPropertyName("unassigned")] bool Unassigned,
    [property: JsonPropertyName("accountErased")] bool AccountErased);

/// <summary>What erasing an account did.</summary>
/// <param name="Erased">Whether the deployment held the account at all.</param>
internal sealed record MailAccountErasure(
    [property: JsonPropertyName("erased")] bool Erased);
