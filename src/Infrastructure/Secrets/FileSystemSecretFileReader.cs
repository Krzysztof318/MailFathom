// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Security;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Reads secret material from the real file system.</summary>
/// <remarks>
/// The type is deliberately a thin opening of a stream: the bounded, erasing read it delegates to lives in
/// <see cref="BoundedSecretMaterialReader" />, which unit tests exercise without touching a file. Every expected
/// file-system failure is mapped onto <see cref="SecretResolutionFailure.MaterialNotFound" /> rather than escaping.
/// Catching fewer of them would let a malformed target — a path containing a NUL character throws
/// <see cref="ArgumentException" /> — travel past the result boundary into an unhandled startup exception whose message
/// quotes the path, defeating both fail-fast aggregation and the guarantee that no diagnostic carries a target.
/// </remarks>
// TODO: Remove this exclusion when the planned host integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Real file-system access will be covered later by host integration tests.")]
internal sealed class FileSystemSecretFileReader : ISecretFileReader
{
    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Every expected file-system failure is translated into one result identity so no target path escapes through an exception message.")]
    public async Task<SecretResolutionResult> ReadAsync(
        string path,
        int maximumByteCount,
        CancellationToken cancellationToken)
    {
        await using var stream = TryOpenForReading(path);

        return stream is null
            ? SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound)
            : await BoundedSecretMaterialReader.ReadAsync(stream, maximumByteCount, cancellationToken);
    }

    /// <summary>Opens the provisioned file, translating every expected failure into an absent stream.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership transfers to the caller, which disposes the stream through an await using block.")]
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
