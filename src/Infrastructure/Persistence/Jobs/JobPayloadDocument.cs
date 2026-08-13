// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Turns a job payload into the document one column holds, and reads it back as the shape it was written as.</summary>
/// <remarks>
/// <para>
/// The job type decides the contract on both sides, which is what removes the discriminator a stored polymorphic
/// document would otherwise need. A document is therefore only ever read back under the type its own row carries, and a
/// type this build does not declare stops the read rather than producing a shape nothing runs.
/// </para>
/// <para>
/// The size bound is applied here because this is the enqueue boundary in the literal sense: it is the one place a
/// payload becomes bytes. It is a constant rather than a setting, because it does not bound capacity — it bounds what a
/// payload is allowed to be, and a deployment that needed a larger one would be a deployment copying content into job
/// state.
/// </para>
/// </remarks>
internal static class JobPayloadDocument
{
    /// <summary>The greatest number of UTF-8 bytes a serialized payload may occupy.</summary>
    /// <remarks>
    /// Generous against every reference this system composes — an account alias, a folder alias, a generation, and two
    /// UIDs come to a couple of hundred bytes — and small enough that a document carrying a subject, an address, or
    /// extracted text is refused rather than stored.
    /// </remarks>
    internal const int MaximumByteCount = 4096;

    /// <summary>Serializes a payload into the document its row holds.</summary>
    /// <param name="payload">The references the work is described by.</param>
    /// <returns>The JSON document to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="payload" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the payload names no declared job type, or a type this store holds no contract for.</exception>
    /// <exception cref="JobPayloadTooLargeException">Thrown when the document exceeds <see cref="MaximumByteCount" />.</exception>
    internal static string Serialize(IJobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.JobType.IsSpecified)
        {
            throw new ArgumentException("A job payload names a declared job type.", nameof(payload));
        }

        var document = payload switch
        {
            EmailOccurrenceJobPayload occurrence =>
                JsonSerializer.Serialize(occurrence, JobPayloadJsonContext.Default.EmailOccurrenceJobPayload),
            _ => throw new ArgumentException(
                $"A '{payload.JobType}' job payload has no serialization contract in this store.",
                nameof(payload)),
        };

        var byteCount = Encoding.UTF8.GetByteCount(document);

        return byteCount <= MaximumByteCount
            ? document
            : throw new JobPayloadTooLargeException(payload.JobType, byteCount, MaximumByteCount);
    }

    /// <summary>Reads a stored document back as the payload contract its job type names.</summary>
    /// <param name="jobType">The type the row carries.</param>
    /// <param name="document">The stored JSON document.</param>
    /// <returns>The payload that document describes.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="jobType" /> is unspecified or <paramref name="document" /> is blank.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the document does not parse as the contract the type names.</exception>
    /// <remarks>
    /// A document that no longer parses is refused rather than repaired, for the reason the payload records refuse a
    /// component that no longer validates: it describes work nothing can perform, and a plausible reconstruction would
    /// point that work at something else.
    /// </remarks>
    internal static IJobPayload Deserialize(JobType jobType, string document)
    {
        if (!jobType.IsSpecified)
        {
            throw new ArgumentException("A stored job document is read back under a declared job type.", nameof(jobType));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(document);

        try
        {
            // Matched with `when` clauses rather than constant patterns, because a closed enumeration's members are
            // static properties and a switch arm cannot pattern-match against one.
            return jobType switch
            {
                _ when jobType == JobType.ClassifyEmailSpam =>
                    JsonSerializer.Deserialize(document, JobPayloadJsonContext.Default.EmailOccurrenceJobPayload)
                        ?? throw new InvalidOperationException(
                            $"A '{jobType}' job carries a document that describes no payload."),
                _ => throw new InvalidOperationException(
                    $"A '{jobType}' job payload has no serialization contract in this store."),
            };
        }
        catch (JsonException failure)
        {
            throw new InvalidOperationException(
                $"A '{jobType}' job carries a document that is not the payload contract of its type.",
                failure);
        }
    }
}
