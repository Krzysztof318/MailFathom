// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Owners;

/// <summary>The owners a deployment holds records for.</summary>
/// <param name="Owners">The owners, in the deployment's own stable order.</param>
/// <remarks>
/// The listing an administrator selects an owner from before doing anything else, which is why every owner-scoped path
/// is composed from what it returns. A deployment serving one person answers with one entry, which is what lets a
/// command act without asking which owner was meant.
/// </remarks>
internal sealed record MailOwnerList(
    [property: JsonPropertyName("owners")] IReadOnlyList<MailOwnerRosterEntry>? Owners);

/// <summary>One owner a deployment holds.</summary>
/// <param name="Id">The identifier the deployment minted for them, which every act names them by.</param>
/// <param name="DisplayName">The label an administrator tells them apart by, which may change and is never the identity.</param>
/// <param name="RecordIsTheirOwn">Whether their mail accounts come from their own record rather than from a configuration source.</param>
/// <param name="Served">Whether the running deployment is serving them, which is false for an owner recorded since it started.</param>
/// <remarks>
/// The label is here because an identifier is what a command needs and a person is what an operator is thinking about;
/// the two flags are here because each is a different thing to act on — one says an adoption still has something to
/// move, and the other says the deployment owes a restart before this owner's mail is read.
/// </remarks>
internal sealed record MailOwnerRosterEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("recordIsTheirOwn")] bool RecordIsTheirOwn,
    [property: JsonPropertyName("served")] bool Served)
{
    /// <summary>States the owner as a line an operator selects from.</summary>
    /// <returns>The identifier with the label beside it, or the identifier alone where the deployment published none.</returns>
    internal string Describe() => string.IsNullOrWhiteSpace(this.DisplayName)
        ? this.Id.ToString("D", null)
        : $"{this.Id:D} ({this.DisplayName})";
}
