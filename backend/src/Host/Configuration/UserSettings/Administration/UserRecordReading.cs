// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Policies;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One user's record as a caller reads it back: the document and the version it stands at.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="DisplayName">The label the deployment tells this user apart by.</param>
/// <param name="Json">The record with every secret-bearing value replaced by the redaction marker.</param>
/// <param name="Version">The version the record was read at, which a change to it is composed over and refused against.</param>
/// <remarks>
/// The document is the record's own: what somebody stated on it, which is what a save is composed over. What the
/// user is served with is answered per property beside it, because a settings policy may force a value over the
/// record's or supply one the record leaves out, and neither is ever written into the document.
/// </remarks>
internal sealed record UserRecordReading(
    UserId User,
    string DisplayName,
    string Json,
    long Version)
{
    /// <summary>Gets every property of the record as the settings policies governing the user resolve it, in path order.</summary>
    /// <remarks>Empty where <see cref="Unresolved" /> says why the record could not be resolved.</remarks>
    public IReadOnlyList<ResolvedProperty> Properties { get; init; } = [];

    /// <summary>Gets the paths of the properties whoever read the record may change.</summary>
    /// <remarks>
    /// Both halves of that are folded in: that no settings policy holds the property against this reader, and that
    /// the reader's grant reaches a write of it. A read needs less than a write does, so a reader told a property was
    /// theirs to change on the policy alone would be offered a control whose change is then refused.
    /// </remarks>
    public IReadOnlyList<string> Changeable { get; init; } = [];

    /// <summary>Gets the sentence saying which settings policy this build does not read, or <see langword="null" /> where the record was resolved.</summary>
    public string? Unresolved { get; init; }
}
