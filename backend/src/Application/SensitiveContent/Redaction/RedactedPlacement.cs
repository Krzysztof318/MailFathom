// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.SensitiveContent.Redaction;

/// <summary>Where one placeholder stands in a redacted text, and how much of the analyzed text it replaced.</summary>
/// <remarks>
/// <para>
/// One per merged region rather than one per finding, because two detectors recognizing the same credential produce one
/// placeholder — so this is the sequence a reader of the redacted text actually meets, in the order they meet it.
/// </para>
/// <para>
/// It carries lengths rather than positions in the redacted text, since every such position follows from the ones
/// before it. What it exists for is <see cref="RedactedText.MapOffset" />: an offset recorded against the text as it
/// was — a page boundary inside an attachment, above all — has to be findable in the text that was actually stored, and
/// a placeholder is shorter or longer than what it replaced.
/// </para>
/// <para>
/// It says nothing about what was removed, exactly as <see cref="RedactedText.Findings" /> does not: a length is not
/// the text, and recreating the leak the redaction prevented is what this whole feature exists to stop.
/// </para>
/// </remarks>
/// <param name="Start">Where the replaced region begins in the text that was analyzed.</param>
/// <param name="Length">How many characters of that text the placeholder replaced.</param>
/// <param name="PlaceholderLength">How many characters the placeholder itself occupies.</param>
public readonly record struct RedactedPlacement(int Start, int Length, int PlaceholderLength);
