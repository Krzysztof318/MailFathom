// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Abandons an extraction the moment one of its ceilings is crossed.</summary>
/// <remarks>
/// A bound is crossed inside a stream a parser is reading or inside a walk several frames below the method that started
/// it, and neither offers a way to stop. Returning a flag would let the parser finish reading the very thing the bound
/// exists to refuse, so the crossing is thrown and caught by the extractor that started the read. The type never leaves
/// this adapter: what a caller sees is the outcome it carries.
/// </remarks>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The type is a control-flow signal between a bounded stream or walk and the extractor that started it, and never crosses the adapter boundary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The exception carries the crossed bound's outcome and is never constructed from a message or an inner exception.")]
[SuppressMessage("Usage", "RCS1194:Implement exception constructors", Justification = "The exception carries the crossed bound's outcome and is never constructed from a message or an inner exception.")]
internal sealed class AttachmentTextExtractionBoundException(AttachmentTextExtractionOutcome outcome)
    : Exception($"The attachment crossed the configured extraction bound reported as {outcome}.")
{
    /// <summary>Gets the outcome the crossed bound is reported as.</summary>
    public AttachmentTextExtractionOutcome Outcome { get; } = outcome;
}
