// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Mailboxes;

/// <summary>What a rewind would have the deployment's next synchronization runs read again.</summary>
/// <param name="Account">The account the assessment is about.</param>
/// <param name="Folder">The alias it was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="StoredEmailCount">How many stored emails the scope holds.</param>
internal sealed record MailboxRewindAssessment(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("storedEmailCount")] int StoredEmailCount)
{
    /// <summary>Describes what a rewind of this scope will cost, for the operator about to agree to it.</summary>
    /// <returns>The sentence to print.</returns>
    /// <remarks>
    /// It names the fetch rather than the row count alone, because the number on its own reads as a count of something
    /// being deleted. Nothing is deleted; what the figure measures is how much mail the deployment will pull over IMAP,
    /// parse, and store again.
    /// </remarks>
    internal string DescribeCost() => this.StoredEmailCount == 0
        ? "The deployment stores no mail for this scope, so nothing would be fetched again."
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{this.StoredEmailCount:N0} stored {(this.StoredEmailCount == 1 ? "email" : "emails")} would be fetched from the mail server, re-read, and stored again.");
}

/// <summary>Which of a scope's folders held durable synchronization progress that the rewind discarded.</summary>
/// <param name="Account">The account the rewind ran against.</param>
/// <param name="Folder">The alias it was narrowed to, or nothing when it covered the whole account.</param>
/// <param name="Folders">The aliases whose bindings held progress, ordered and without repeats.</param>
internal sealed record MailboxRewind(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("folder")] string? Folder,
    [property: JsonPropertyName("folders")] IReadOnlyList<string>? Folders);
