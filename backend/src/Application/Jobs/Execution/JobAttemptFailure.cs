// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>What one failed attempt was classified as, what was written against the job, and what became of it.</summary>
/// <remarks>
/// <strong>The exception a handler raised is deliberately not here.</strong> A handler works on mail, so a library's
/// message may quote a subject, an address, or a header, and anything holding that exception would put it into every
/// log line reporting the attempt. <see cref="Record" /> is the narrowed form — a type name and a stable code — and it
/// is what both the row and the report carry. A handler that wants its own failure diagnosed in full is the one place
/// that knows what in it is safe to write, so it logs that itself.
/// </remarks>
/// <param name="Record">The verdict and the operator-safe reason the job's row now keeps.</param>
/// <param name="Disposition">Whether the job goes back to the queue or is terminal.</param>
public sealed record JobAttemptFailure(JobFailureRecord Record, JobFailureDisposition Disposition)
{
    /// <summary>Gets the instant the job becomes claimable again, and <see langword="null" /> when it was dead-lettered.</summary>
    public DateTimeOffset? NextAttemptAt { get; init; }
}
