// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Persistence;

/// <summary>Indicates that the schema state could not be established, so the schema's shape is unknown.</summary>
/// <remarks>
/// <para>
/// Unknown is deliberately its own failure rather than a third value folded into "pending" or "current". An
/// unreachable server, a database that has never been created, and a user without rights on the history table all
/// leave the same question unanswered, and answering it either way would let an instance start against a schema
/// nothing inspected.
/// </para>
/// <para>
/// A catalogue that answers without identifying anything is the same failure. A search vector column that is absent,
/// carries no stored expression, or carries one naming no registered configuration leaves the running build unable to
/// say which configuration its lexemes were built with, and it arrives once every migration is applied — so it
/// describes a database those migrations did not produce rather than a fact that is merely unknown yet.
/// </para>
/// <para>
/// The message names the migration history and the reason class only. The provider's own text can carry a host name, a
/// user name, and a database name, so it is preserved as <see cref="Exception.InnerException" /> rather than restated
/// in a message an MCP or HTTP boundary may publish.
/// </para>
/// <para>
/// The inner exception is deliberate and does reach a log, including an exported one: this failure ends the process
/// during startup, and which server was unreachable and as which user is the whole content of the diagnosis. That is
/// the division <see cref="MailFathomException" /> defines — the message is what a boundary may publish, the inner
/// exception is diagnostic detail for a log — and it is compatible with the repository's logging rule, which forbids
/// credentials, tokens, message bodies, attachment content, and raw MIME. A connection endpoint is none of those, it
/// is infrastructure topology the operator configured, and Npgsql does not put the password in its text. Dropping it
/// would leave an unreachable database reported only as "unreadable".
/// </para>
/// </remarks>
public sealed class DatabaseSchemaStateUnreadableException : MailFathomException
{
    /// <summary>Initializes a new unreadable-schema failure that preserves the provider's own failure.</summary>
    /// <param name="operatorSafeMessage">A message free of host names, user names, and provider text.</param>
    /// <param name="innerException">The provider failure that prevented the read.</param>
    public DatabaseSchemaStateUnreadableException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <summary>Initializes a new unreadable-schema failure the catalogue reported rather than failed on.</summary>
    /// <param name="operatorSafeMessage">A message naming which schema fact could not be established.</param>
    /// <remarks>
    /// There is no provider failure to preserve here: the query succeeded, and what it returned identifies no schema
    /// this build recognizes. The message carries the whole diagnosis, which is why this overload exists rather than
    /// an invented inner exception standing in for one.
    /// </remarks>
    public DatabaseSchemaStateUnreadableException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.DatabaseSchemaStateUnreadable;
}
