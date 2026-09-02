// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Persistence;

/// <summary>Indicates that the lexical index was built with a different text search configuration than the configured one.</summary>
/// <remarks>
/// <para>
/// The configuration is compiled into a stored generated column when the table is created, so the lexemes in the index
/// were produced by whichever configuration the applied migration named. A process configured for another one stems
/// its queries one way and reads lexemes built the other, which returns fewer results rather than an error — the worst
/// shape a search defect can take, because nothing distinguishes it from a mailbox that genuinely holds no match.
/// </para>
/// <para>
/// Both names are MailFathom's own configured names for PostgreSQL text search configurations, so reporting them carries
/// no credential, host name, or personal data.
/// </para>
/// </remarks>
public sealed class DatabaseSchemaTextSearchConfigurationMismatchException : MailFathomException
{
    /// <summary>Initializes a new text search configuration mismatch between the schema and the configuration.</summary>
    /// <param name="operatorSafeMessage">A message naming both configurations and how to reconcile them.</param>
    /// <param name="schemaConfiguration">The configuration the lexical index was actually built with.</param>
    /// <param name="configuredConfiguration">The configuration this process was configured to use.</param>
    public DatabaseSchemaTextSearchConfigurationMismatchException(
        string operatorSafeMessage,
        string schemaConfiguration,
        string configuredConfiguration)
        : base(operatorSafeMessage)
    {
        this.SchemaConfiguration = schemaConfiguration;
        this.ConfiguredConfiguration = configuredConfiguration;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.DatabaseSchemaTextSearchConfigurationMismatch;

    /// <summary>Gets the text search configuration the lexical index was built with.</summary>
    public string SchemaConfiguration { get; }

    /// <summary>Gets the text search configuration this process was configured to use.</summary>
    public string ConfiguredConfiguration { get; }
}
