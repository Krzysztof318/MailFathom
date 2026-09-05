// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Abandons an extraction from inside the read, carrying the outcome it is to be reported as.</summary>
/// <remarks>
/// A read stops inside a stream a parser is reading or inside a walk several frames below the method that started it,
/// and neither offers a way to stop. Returning a flag would let the parser finish reading the very thing the stop
/// exists to refuse, so it is thrown and caught by the extractor that started the read. Most of what stops a read this
/// way is a crossed ceiling; a package that turns out to be encrypted is the one that is not, which is why the type is
/// named after stopping rather than after a bound. It never leaves this adapter: what a caller sees is the outcome.
/// </remarks>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The type is a control-flow signal between a bounded stream or walk and the extractor that started it, and never crosses the adapter boundary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The exception carries the outcome the read stopped at and is never constructed from a message or an inner exception.")]
[SuppressMessage("Usage", "RCS1194:Implement exception constructors", Justification = "The exception carries the outcome the read stopped at and is never constructed from a message or an inner exception.")]
internal sealed class AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome outcome)
    : Exception($"The attachment's extraction stopped and is reported as {outcome}.")
{
    /// <summary>Gets the outcome the stopped read is reported as.</summary>
    public AttachmentTextExtractionOutcome Outcome { get; } = outcome;
}
