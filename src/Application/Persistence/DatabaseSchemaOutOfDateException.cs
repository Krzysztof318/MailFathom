// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Failures;

namespace MailMcp.Application.Persistence;

/// <summary>Indicates that the database is missing migrations the running build was compiled against.</summary>
/// <remarks>
/// <para>
/// This is raised rather than returned as a result because no caller above it can decide what a stale schema means.
/// The only correct response is to stop: an instance that served traffic against a schema it does not recognize could
/// write mail data into a shape the deletion and retention paths do not reach, and would do so silently.
/// </para>
/// <para>
/// The message names migration identifiers only. Those are MailMcp's own build-time names for schema versions and
/// carry no credential, host name, or personal data.
/// </para>
/// </remarks>
public sealed class DatabaseSchemaOutOfDateException : MailMcpException
{
    /// <summary>Initializes a new stale-schema failure for the migrations the database has not applied.</summary>
    /// <param name="operatorSafeMessage">A message naming migration identifiers and the command that applies them.</param>
    /// <param name="pendingMigrationIdentifiers">The migrations the build defines that the database does not carry.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="pendingMigrationIdentifiers" /> is <see langword="null" />.</exception>
    public DatabaseSchemaOutOfDateException(
        string operatorSafeMessage,
        IReadOnlyList<string> pendingMigrationIdentifiers)
        : base(operatorSafeMessage)
    {
        ArgumentNullException.ThrowIfNull(pendingMigrationIdentifiers);

        this.PendingMigrationIdentifiers = pendingMigrationIdentifiers;
    }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.DatabaseSchemaOutOfDate;

    /// <summary>Gets the migrations the build defines that the database does not carry, in the order they would be applied.</summary>
    public IReadOnlyList<string> PendingMigrationIdentifiers { get; }
}
