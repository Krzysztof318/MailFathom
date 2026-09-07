// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Names why a screened egress was stopped rather than served.</summary>
/// <remarks>
/// Three reasons rather than one, because each leaves whoever wrote the message with different work to do: the first
/// asks them to take something out of it, the second asks them to make it shorter or asks the operator to raise the
/// analyzed ceiling, and the third asks them to attach the file in a form something can read. A single reason would
/// tell the second author to look for material that was never found, and the third to look inside a body that was
/// scanned and came back clean.
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

    /// <summary>A file the act would have carried out of the deployment could not be read, so nothing established what it holds.</summary>
    /// <remarks>
    /// The one reason that is not about text at all. An encrypted document, one in a format nothing here parses, one
    /// whose bytes do not parse as what they declare, and one whose reading ran past a ceiling are the same fact to a
    /// screen: the file is going out and nobody knows what is in it. Treating that as clean would make the whole screen
    /// a property of the format a sender chose.
    /// </remarks>
    AttachmentNotRead = 2,
}
