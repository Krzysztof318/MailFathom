// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Mailboxes;

/// <summary>What a deployment is asked when stored mail is to be brought up to a newer release's properties.</summary>
/// <param name="Account">The account to act on, as the deployment's configuration names it.</param>
/// <param name="Folder">MailFathom's own alias for the one folder to act on, or nothing for every folder the account holds mail in.</param>
/// <remarks>
/// One shape for both operations, because an operator names the same two things for either. Which operation is meant is
/// the route rather than a field here, so a mistyped value cannot be the difference between re-reading local bytes and
/// pulling a mailbox over IMAP again.
/// </remarks>
internal sealed record MailboxMaintenanceRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("folder")] string? Folder);
