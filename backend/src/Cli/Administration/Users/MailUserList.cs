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
/// <param name="RecordIsTheirOwn">Whether their mail accounts come from their own record rather than from a configuration source.</param>
/// <param name="Served">Whether the running deployment is serving them.</param>
/// <param name="DeclaredInConfiguration">Whether the deployment's own mail section supplies their mailboxes, so a start writes their row again after an erasure.</param>
/// <remarks>
/// The label is here because an identifier is what a command needs and a person is what an operator is thinking about;
/// the three flags are here because each is a different thing to act on — the first says whether a change to their
/// mail accounts is written into their record or into the deployment's own section, the second says whether this
/// user's mail is read now, and the third says that section is where this user's mailboxes are changed and cleared
/// before they can be erased.
/// </remarks>
internal sealed record MailUserRosterEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("recordIsTheirOwn")] bool RecordIsTheirOwn,
    [property: JsonPropertyName("served")] bool Served,
    [property: JsonPropertyName("declaredInConfiguration")] bool DeclaredInConfiguration)
{
    /// <summary>States the user as a line an operator selects from.</summary>
    /// <returns>The identifier with the label beside it, or the identifier alone where the deployment published none.</returns>
    internal string Describe() => string.IsNullOrWhiteSpace(this.DisplayName)
        ? this.Id.ToString("D", null)
        : $"{this.Id:D} ({this.DisplayName})";
}
