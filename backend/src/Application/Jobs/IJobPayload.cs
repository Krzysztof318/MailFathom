// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Describes the work one job has to do, in references rather than in copies.</summary>
/// <remarks>
/// <para>
/// A payload names a message occurrence, an account, a folder, or a rule identity. It never copies a subject, a body,
/// an address, or extracted text: job state must not become a second uncontrolled copy of personal data with retention,
/// export, and erasure obligations of its own. Nothing bounds that but the review of each payload record when its job
/// type is added, which is why it is stated on the contract itself rather than assumed.
/// </para>
/// <para>
/// Each declared <see cref="JobType" /> names exactly one implementation, and the implementation names its type back.
/// That is what lets a stored document be read as the shape it was written as, with no discriminator invented for the
/// purpose, and what lets a payload handed to the wrong type be refused where the job is composed rather than where it
/// is read back.
/// </para>
/// </remarks>
public interface IJobPayload
{
    /// <summary>Gets the job type this payload is the one contract of.</summary>
    /// <remarks>It is not part of the stored document, because the type is already a column of the row and a second copy could disagree with it.</remarks>
    JobType JobType { get; }
}
