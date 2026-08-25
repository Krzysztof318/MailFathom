// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;

namespace MailFathom.Application.Observability;

/// <summary>Holds one pass's report open while it runs, and takes each payload's outcome as the pass reaches it.</summary>
/// <remarks>
/// The report is open <em>around</em> the pass rather than written after it, so the database and endpoint work the pass
/// causes is reported beneath it. A payload is reported the moment it is decided rather than at the end, because a pass
/// stopped by a shutdown has still moved everything it repointed and an operator watching the counters must see it.
/// </remarks>
public interface IStoredContentMovePassScope : IDisposable
{
    /// <summary>Records one payload the move copied, verified, and repointed at the object.</summary>
    /// <param name="byteLength">How many bytes of raw MIME it carried.</param>
    void Copied(long byteLength);

    /// <summary>Records one payload the move left in the database, and why.</summary>
    /// <param name="failure">What stopped it, which is what an operator acts on.</param>
    void Failed(StoredContentMoveFailure failure);

    /// <summary>Records that the pass reached the end of the last payload kind, which is what ends the move.</summary>
    void ReachedEndOfContent();
}
