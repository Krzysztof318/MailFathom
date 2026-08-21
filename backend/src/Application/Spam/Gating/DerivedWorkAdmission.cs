// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Gating;

/// <summary>States what classification says about one occurrence's readiness for the work derived from it.</summary>
/// <remarks>
/// <para>
/// Three answers rather than two, and the third is the whole reason this is an enumeration instead of a predicate. A
/// message still waiting on a verdict has not failed, so a rule reading only <em>failed</em> would never release one and
/// a classification backlog deep enough to matter would stop the index instead of delaying it. Waiting, settled, and
/// released are therefore distinct, and only the first withholds indefinitely.
/// </para>
/// <para>
/// Every member is derived from where the message is now and what was decided about it. Nothing writes it down, which is
/// what makes mail moved back out of the junk folder eligible again without anything having to notice the move.
/// </para>
/// </remarks>
public enum DerivedWorkAdmission
{
    /// <summary>Classification settled the message as ordinary correspondence, or it never reached the message at all.</summary>
    Admitted = 0,

    /// <summary>The message is junk, so nothing downstream of classification runs for it.</summary>
    WithheldAsJunk = 1,

    /// <summary>No verdict has been reached yet and the wait a verdict is allowed has not run out.</summary>
    AwaitingClassification = 2,

    /// <summary>No verdict will ever be reached, because the message carries nothing a classification could read.</summary>
    ReleasedAsUnclassifiable = 3,

    /// <summary>No verdict has been reached and the message has waited longer than a verdict is allowed to take.</summary>
    ReleasedAfterWaiting = 4,
}
