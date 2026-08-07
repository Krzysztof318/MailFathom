// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.Secrets.Sources;

/// <summary>Reads secret material from the real file system.</summary>
/// <remarks>
/// The type is deliberately the platform call and nothing else. Both bounds around it are unit-testable without a file:
/// <see cref="BoundedSecretFileRetrieval" /> owns the deadline and the limit on how many opens may be in flight, and
/// <see cref="BoundedSecretMaterialReader" /> owns the erasing read, the size ceiling, and the rejection of a target
/// that is not a regular file. Every expected file-system failure is mapped onto
/// <see cref="SecretResolutionFailure.MaterialNotFound" /> rather than escaping. Catching fewer of them would let a
/// malformed target — a path containing a NUL character throws <see cref="ArgumentException" /> — travel past the
/// result boundary into an unhandled startup exception whose message quotes the path, defeating both fail-fast
/// aggregation and the guarantee that no diagnostic carries a target.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class FileSystemSecretFileReader(TimeProvider timeProvider) : ISecretFileReader, IDisposable
{
    private readonly BoundedSecretFileRetrieval retrieval = new(timeProvider);

    /// <inheritdoc />
    public Task<SecretResolutionResult> ReadAsync(
        string path,
        int maximumByteCount,
        CancellationToken cancellationToken) =>
        this.retrieval.ReadAsync(() => TryOpenForReading(path), maximumByteCount, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => this.retrieval.Dispose();

    /// <summary>Opens the provisioned file, translating every expected failure into an absent stream.</summary>
    /// <remarks>
    /// This runs on a thread the retrieval may stop waiting for, so it opens synchronously and reports through its
    /// return value: a token would reach neither the kernel call nor the caller that has already given up on it.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership transfers to the retrieval, which disposes the stream whether or not it is still waiting for it.")]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every expected file-system failure is translated into one result identity so no target path escapes through an exception message.")]
    private static FileStream? TryOpenForReading(string path)
    {
        try
        {
            return new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous,
                });
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException)
        {
            return null;
        }
    }
}
