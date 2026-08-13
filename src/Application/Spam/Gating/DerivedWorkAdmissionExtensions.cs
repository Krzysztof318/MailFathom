// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Gating;

/// <summary>Reads an admission as the one question every consumer of the gate actually asks.</summary>
/// <remarks>
/// Three of the five answers permit derived work and two do not, and writing that split out at each call site is how
/// the two released answers eventually get treated as a held message somewhere. Naming it once is what keeps a wedged
/// scanner from silently stopping the index in one path while releasing correctly in another.
/// </remarks>
public static class DerivedWorkAdmissionExtensions
{
    /// <summary>Reports whether chunking, embedding, and rule evaluation may run for the occurrence.</summary>
    /// <param name="admission">What the gate concluded.</param>
    /// <returns><see langword="false" /> only for junk and for a message still inside its wait.</returns>
    public static bool PermitsDerivedWork(this DerivedWorkAdmission admission) => admission
        is DerivedWorkAdmission.Admitted
        or DerivedWorkAdmission.ReleasedAsUnclassifiable
        or DerivedWorkAdmission.ReleasedAfterWaiting;
}
