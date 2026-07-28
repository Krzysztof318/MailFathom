// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Application.Emails;

/// <summary>Bounds the structure a message may declare before extraction abandons it.</summary>
/// <remarks>
/// A size limit alone does not bound the work: a message far below the raw MIME limit can declare tens of thousands of
/// parts or nest multiparts hundreds deep, which is an inexpensive way to consume disproportionate CPU and allocations.
/// Both limits are enforced while the message is being read, so an over-limit message is abandoned before an object
/// tree exists for it.
/// </remarks>
public sealed class EmailMimeExtractionOptions
{
    /// <summary>Gets or sets the greatest number of MIME entities one message may declare.</summary>
    public int MaxPartCount { get; set; } = 1000;

    /// <summary>Gets or sets the greatest depth to which one message may nest multiparts and embedded messages.</summary>
    public int MaxNestingDepth { get; set; } = 30;
}
