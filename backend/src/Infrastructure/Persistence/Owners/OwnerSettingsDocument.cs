// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>One owner's persisted record: the envelope the deployment keys them by, and the document they configured.</summary>
/// <param name="Owner">The owner the row belongs to.</param>
/// <param name="DisplayName">The label the deployment tells this owner apart by, which nothing resolves them by.</param>
/// <param name="Json">The owner's configurable record, as the JSON object the row holds.</param>
/// <param name="Version">The version the document was read at, which a writer states and is refused against.</param>
/// <param name="WrittenAtRuntime">Whether anything has written the document, which is what tells an unfilled row from an emptied one.</param>
/// <remarks>
/// The envelope travels with the document because the two answer different halves of one question and a caller that
/// read one would immediately ask for the other: the document says what this owner configured, and the version beside
/// it says which document a change to it would be composed over. Reading them apart would leave a writer stating a
/// version it read in a second query, which is the race the version exists to refuse.
/// </remarks>
public sealed record OwnerSettingsDocument(
    MailOwnerId Owner,
    string DisplayName,
    string Json,
    long Version,
    bool WrittenAtRuntime)
{
    /// <summary>The largest owner document this build binds, and therefore the largest one it will persist.</summary>
    /// <remarks>
    /// <para>
    /// <c>jsonb</c> holds up to a gigabyte, and this document is expanded three times over on its way to a bound
    /// aggregate — the string the driver materializes, the UTF-8 bytes the parser is handed, and the flattened
    /// dictionary the binder reads. A ceiling an owner's declarations could plausibly reach would be the wrong
    /// ceiling; this one is far past every mail account and owner-level setting one person configures, and far below
    /// anything the bind costs a thought, so a row past it is a row something went wrong with.
    /// </para>
    /// <para>
    /// One bound rather than two, for the reason the deployment's document states: a write permitted past what the
    /// bind accepts would persist a record the next read refuses, and the owner would be locked out by a change that
    /// had been accepted. It is applied in both places the document is expanded — in the statement that reads the row,
    /// where PostgreSQL measures the column and declines to send it, and in the binder, which is where a candidate
    /// nothing has persisted yet is measured. Both measure the same value, which is the rendering the database
    /// stores rather than the compact form a candidate is composed as: the binder gets it from
    /// <c>RootSettingsCommitRules.PersistedOctetsOf</c>, whose own remark says what measuring the compact form would
    /// cost.
    /// </para>
    /// </remarks>
    public const int MaximumOctets = 1024 * 1024;
}
