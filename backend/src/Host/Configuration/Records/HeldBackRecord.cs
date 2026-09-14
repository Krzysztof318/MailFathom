// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Records;

/// <summary>One stored record this replica refused, and what has to change before it is served again.</summary>
/// <param name="Kind">Which kind of record it is, which says what the refusal costs.</param>
/// <param name="Identity">The identifier the deployment generated for the record, which is what an administrative act names it by.</param>
/// <param name="Label">The operator's own text for the record, which is what they recognize it by in a listing.</param>
/// <param name="RejectedVersion">The version that was refused, or <see langword="null" /> for a kind of record that carries no version.</param>
/// <param name="Corrections">One sentence per setting to correct, in the words the write to it would have been refused with.</param>
/// <remarks>
/// The identifier and the label travel together because they answer different halves of one question: the label is
/// what an operator reads the record by and the identifier is what they pass to the command that repairs it. Neither
/// carries a secret, mail content, or an address — the label is the name the operator themselves recorded, and the
/// corrections are MailFathom's own sentences about settings rather than the values beside them.
/// </remarks>
internal sealed record HeldBackRecord(
    HeldBackRecordKind Kind,
    Guid Identity,
    string Label,
    long? RejectedVersion,
    IReadOnlyList<string> Corrections);
