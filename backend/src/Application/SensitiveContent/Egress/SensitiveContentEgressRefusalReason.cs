// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Names why a screened egress was stopped rather than served.</summary>
/// <remarks>
/// Two reasons rather than one, because the two leave whoever wrote the message with different work to do: the first
/// asks them to take something out of it, and the second asks them to make it shorter or asks the operator to raise the
/// analyzed ceiling. A single reason would tell the second author to look for material that was never found.
/// </remarks>
public enum SensitiveContentEgressRefusalReason
{
    /// <summary>A switched-on scanner found material of a category this egress point is screened for.</summary>
    ContentFound = 0,

    /// <summary>The text ran past the analyzed ceiling, so nothing established what its remainder carries.</summary>
    /// <remarks>
    /// The ceiling is the one bound in this feature that a redacting guard never raises a failure over — text beyond it
    /// is dropped from what that guard hands on, which loses the remainder and never publishes it. A screen has nothing
    /// to drop: what it is asked is whether this whole message may leave, and a message whose tail was never analyzed
    /// has no answer to that question except no.
    /// </remarks>
    TextExceededScanCeiling = 1,
}
