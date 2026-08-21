// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Holds part of <see cref="RawMimeMemoryBudget" /> for as long as one payload is buffered.</summary>
/// <remarks>
/// Disposal is what returns the bytes, so a reservation is taken with <c>using</c> in the scope that holds the
/// payload and never stored beyond it. Disposing twice returns them once, because a reservation released twice would
/// enlarge the budget rather than restore it.
/// </remarks>
public sealed class RawMimeMemoryReservation : IDisposable
{
    private RawMimeMemoryBudget? budget;

    internal RawMimeMemoryReservation(RawMimeMemoryBudget budget, long bytes)
    {
        this.budget = budget;
        this.Bytes = bytes;
    }

    /// <summary>Gets how many bytes of the budget this reservation holds.</summary>
    public long Bytes { get; }

    /// <summary>Returns the reserved bytes to the budget.</summary>
    public void Dispose()
    {
        var released = Interlocked.Exchange(ref this.budget, null);

        released?.Release(this.Bytes);
    }
}
