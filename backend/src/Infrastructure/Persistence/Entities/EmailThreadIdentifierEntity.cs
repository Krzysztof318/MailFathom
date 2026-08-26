// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Binds one message identifier an account's mail refers to, to the thread that identifier belongs to.</summary>
/// <remarks>
/// <para>
/// This is what makes a thread hold an ancestor nobody stored. A reply names its parent by an identifier whether or not
/// this deployment ever received that parent, so binding the identifier rather than the row is what puts two replies to
/// an absent message into one conversation instead of two.
/// </para>
/// <para>
/// It is also the lookup the whole assembly runs on. Every identifier one arriving message carries is resolved here in
/// a single query, and the threads that come back are the threads that message joins or merges.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailThreadIdentifierEntity
{
    /// <summary>The width of the digest that stands in for the identifier, which is SHA-256's in lower-case hexadecimal.</summary>
    internal const int IdentifierHashLength = 64;

    /// <summary>Gets or sets the account the identifier was seen in.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account the identifier was seen in.</summary>
    public required Guid OwnerId { get; set; }

    /// <summary>Gets or sets the digest of the message identifier this row binds.</summary>
    /// <remarks>
    /// <para>
    /// A digest rather than the identifier itself, because this column is a key and the identifier is not one a key can
    /// hold: nothing between a sender and this system bounds a header, the stored form accepts the 998 octets RFC 5322
    /// allows a header line, and a B-tree entry that wide is one PostgreSQL refuses at insert time. A refused insert
    /// here would fail the arrival transaction, so the value that is indexed is fixed-width by construction.
    /// </para>
    /// <para>
    /// Truncating the identifier instead was refused for the reason the mapping refuses to truncate one anywhere: a
    /// prefix of a message identifier is an identifier another message may legitimately carry, and a conversation
    /// assembled from it would join two exchanges that never touched.
    /// </para>
    /// <para>
    /// Nothing reads the identifier back, so it is not stored beside the digest. That is data minimization rather than
    /// economy: the identifier is already on the email's own row, and a second copy of it would widen the access,
    /// export, and erasure surface for a value this table only ever compares.
    /// </para>
    /// <para>
    /// It is held as hexadecimal text rather than as <c>bytea</c>, because the lookup resolves every identifier one
    /// arriving message carries in a single query and a set membership over text is a translation the provider gives
    /// the same plan for without either side of it depending on how a byte array parameter is bound.
    /// </para>
    /// </remarks>
    public required string IdentifierHash { get; set; }

    /// <summary>Gets or sets the thread the identifier belongs to.</summary>
    public Guid EmailThreadId { get; set; }
}
