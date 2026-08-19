// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction;

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

    /// <summary>Gets or sets whether extraction verifies a message's own DKIM signatures where no trusted server did.</summary>
    /// <remarks>
    /// <para>
    /// It defaults to on because the deployment it exists for is one whose receiving server writes no
    /// <c>Authentication-Results</c> header at all. There, the sender verdict is not established on every message and
    /// the trusted-sender list has no identity to match against, so this is not an extra check over a working verdict —
    /// it is the only thing between that mailbox and a verdict that says nothing.
    /// </para>
    /// <para>
    /// It is a fallback and never a supplement: an account whose server does write the header goes on believing that
    /// server and verifies nothing here, whatever this says.
    /// </para>
    /// <para>
    /// Turning it off is what an operator who wants no egress at all from the extraction path sets, and it returns
    /// exactly the behaviour of a deployment that never had it. What is on the wire when it is on is
    /// <c>&lt;selector&gt;._domainkey.&lt;domain&gt;</c> — a name the signing domain published to be asked for, resolved
    /// when a message is stored rather than when one is read.
    /// </para>
    /// </remarks>
    public bool VerifyDkimLocally { get; set; } = true;
}
