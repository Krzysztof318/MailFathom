// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Users;

/// <summary>The users a deployment holds records for.</summary>
/// <param name="Users">The users, in the deployment's own stable order.</param>
/// <remarks>
/// The listing an administrator selects a user from before doing anything else, which is why every user-scoped path
/// is composed from what it returns. A deployment serving one person answers with one entry, which is what lets a
/// command act without asking which user was meant.
/// </remarks>
internal sealed record MailUserList(
    [property: JsonPropertyName("users")] IReadOnlyList<MailUserRosterEntry>? Users);

/// <summary>One user a deployment holds.</summary>
/// <param name="Id">The identifier the deployment minted for them, which every act names them by.</param>
/// <param name="DisplayName">The label an administrator tells them apart by, which may change and is never the identity.</param>
/// <param name="Served">Whether the running deployment is serving them, which is whether this user's mail is read now.</param>
/// <remarks>
/// The label is here because an identifier is what a command needs and a person is what an operator is thinking about.
/// </remarks>
internal sealed record MailUserRosterEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("served")] bool Served)
{
    /// <summary>States the user as a line an operator selects from.</summary>
    /// <returns>The identifier with the label beside it, or the identifier alone where the deployment published none.</returns>
    internal string Describe() => string.IsNullOrWhiteSpace(this.DisplayName)
        ? this.Id.ToString("D", null)
        : $"{this.Id:D} ({this.DisplayName})";
}
