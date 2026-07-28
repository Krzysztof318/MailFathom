// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Emails;

/// <summary>Bounds the structure a message may declare, and the text it may contribute, before extraction abandons it.</summary>
/// <remarks>
/// A size limit alone does not bound the work: a message far below the raw MIME limit can declare tens of thousands of
/// parts or nest multiparts hundreds deep, which is an inexpensive way to consume disproportionate CPU and allocations.
/// Both structural limits are enforced while the message is being read, so an over-limit message is abandoned before an
/// object tree exists for it.
/// </remarks>
public sealed class EmailMimeExtractionOptions
{
    /// <summary>Gets or sets the greatest number of MIME entities one message may declare.</summary>
    public int MaxPartCount { get; set; } = 1000;

    /// <summary>Gets or sets the greatest depth to which one message may nest multiparts and embedded messages.</summary>
    public int MaxNestingDepth { get; set; } = 30;

    /// <summary>Gets or sets the greatest number of characters one message's body contributes to the extracted text.</summary>
    /// <remarks>
    /// The bound exists because the extracted text is indexed. A PostgreSQL <c>tsvector</c> cannot exceed one megabyte,
    /// and the generated column that builds it is part of every insert, so an unbounded body would not degrade search —
    /// it would make the row unwritable and stop the folder it arrived in. The default leaves ample room for the
    /// subject and participant addresses that share the same document.
    /// </remarks>
    public int MaxExtractedTextCharacters { get; set; } = 100_000;
}
