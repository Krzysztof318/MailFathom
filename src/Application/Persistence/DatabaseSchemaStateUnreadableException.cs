// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Failures;

namespace MailMcp.Application.Persistence;

/// <summary>Indicates that the migration history could not be read, so the schema's shape is unknown.</summary>
/// <remarks>
/// <para>
/// Unknown is deliberately its own failure rather than a third value folded into "pending" or "current". An
/// unreachable server, a database that has never been created, and a user without rights on the history table all
/// leave the same question unanswered, and answering it either way would let an instance start against a schema
/// nothing inspected.
/// </para>
/// <para>
/// The message names the migration history and the reason class only. The provider's own text can carry a host name, a
/// user name, and a database name, so it is preserved as <see cref="Exception.InnerException" /> for a local operator
/// rather than restated in a message a boundary may publish.
/// </para>
/// </remarks>
public sealed class DatabaseSchemaStateUnreadableException : MailMcpException
{
    /// <summary>Initializes a new unreadable-schema failure that preserves the provider's own failure.</summary>
    /// <param name="operatorSafeMessage">A message free of host names, user names, and provider text.</param>
    /// <param name="innerException">The provider failure that prevented the read.</param>
    public DatabaseSchemaStateUnreadableException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.DatabaseSchemaStateUnreadable;
}
