// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>Text a guard cleared, together with what the analyzed ceiling kept out of it.</summary>
/// <param name="Text">The text as it may now be handed on, with every detected region replaced.</param>
/// <param name="OmittedCharacterCount">How many characters lay beyond what one scan analyzes and were therefore dropped.</param>
/// <remarks>
/// <para>
/// Most consumers need only the text, because the thing they publish carries no statement about its own completeness: a
/// snippet is already an extract and a prompt is composed rather than measured. A consumer that does make such a
/// statement — a body representation naming the bound that cut it — has to know that the ceiling cut, or it would
/// publish a message ending mid-sentence and call it whole.
/// </para>
/// <para>
/// The findings are deliberately not here. What was detected stays inside the guard and is published as counts by
/// category, so no consumer can log, store, or forward the thing the redaction removed.
/// </para>
/// </remarks>
public sealed record GuardedText(string Text, int OmittedCharacterCount)
{
    /// <summary>Gets whether the analyzed ceiling dropped anything from the text.</summary>
    public bool WasCutAtAnalyzedCeiling => this.OmittedCharacterCount > 0;
}
