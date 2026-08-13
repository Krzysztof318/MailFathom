// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Summarizes one bounded pass of the cut that follows the rules.</summary>
/// <param name="ChunkedEmailCount">How many messages had their passages cut by this pass.</param>
/// <param name="RefusedOfferCount">How many of those the embedding backlog's bound turned away, which the backfill reaches instead.</param>
/// <param name="EmailsRemain">Whether messages were still awaiting the cut when the pass spent its batch budget.</param>
/// <remarks>
/// Every field is a count. Nothing derived from a message — a subject, an address, a passage — belongs in a result a
/// worker logs.
/// </remarks>
public sealed record MailChunkingPassReport(int ChunkedEmailCount, int RefusedOfferCount, bool EmailsRemain)
{
    /// <summary>Gets whether this pass did anything worth an operator's attention.</summary>
    public bool IsEmpty => this.ChunkedEmailCount == 0 && this.RefusedOfferCount == 0;
}
