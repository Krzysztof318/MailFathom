// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Jobs;

/// <summary>Indicates that a job payload serialized to more than the enqueue boundary accepts.</summary>
/// <remarks>
/// <para>
/// A payload holds references, and every reference this system composes is short. A document that exceeds the bound is
/// therefore evidence that something copied content into job state, which is the outcome the payload contract exists to
/// prevent — so the enqueue is refused rather than truncated or stored.
/// </para>
/// <para>
/// It is raised rather than reported as a result because no caller can act on it: an enqueuer composing an oversized
/// document has a defect to fix, not a smaller document to try instead. The message names the job type, the bound, and
/// the size, and carries nothing out of the document.
/// </para>
/// </remarks>
public sealed class JobPayloadTooLargeException : MailFathomException
{
    /// <summary>Initializes a new refusal naming the type, the bound, and the size that exceeded it.</summary>
    /// <param name="jobType">The job type whose payload was composed.</param>
    /// <param name="serializedByteCount">How many bytes the serialized document occupies.</param>
    /// <param name="maximumByteCount">The greatest number of bytes the enqueue boundary accepts.</param>
    public JobPayloadTooLargeException(JobType jobType, int serializedByteCount, int maximumByteCount)
        : base($"A '{jobType}' job payload serialized to {serializedByteCount} bytes, " +
            $"which is more than the {maximumByteCount} bytes a job payload may occupy.")
    {
        this.JobType = jobType;
        this.SerializedByteCount = serializedByteCount;
        this.MaximumByteCount = maximumByteCount;
    }

    /// <summary>Gets the job type whose payload was composed.</summary>
    public JobType JobType { get; }

    /// <summary>Gets how many bytes the serialized document occupies.</summary>
    public int SerializedByteCount { get; }

    /// <summary>Gets the greatest number of bytes the enqueue boundary accepts.</summary>
    public int MaximumByteCount { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.JobPayloadTooLarge;
}
