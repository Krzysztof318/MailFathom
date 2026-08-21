// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Configures deployment-wide local persistence behavior.</summary>
/// <remarks>
/// Optimistic concurrency is bound once for the whole deployment rather than per use case, so every writer that
/// competes for the same PostgreSQL rows shares one operational limit.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class PersistenceOptions : IValidatableObject
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Persistence";

    /// <summary>Gets or sets the maximum number of complete local write attempts after concurrency conflicts, including the first attempt.</summary>
    [Range(1, 10)]
    public int MaximumConcurrencyCommitAttempts { get; set; } = 2;

    /// <summary>Gets or sets the PostgreSQL text search configuration the lexical email index is built with.</summary>
    /// <remarks>
    /// It decides how every indexed word is stemmed and which words are dropped as stop words, so it is part of the
    /// schema rather than of a query: changing it changes what the index contains and needs the search documents
    /// rebuilt. Startup fails on a configuration a stock PostgreSQL server does not ship, because the value is
    /// compiled into a generated column and a name that only fails at schema creation would report the mistake far
    /// from where it was made.
    /// </remarks>
    public string TextSearchConfiguration { get; set; } = PostgresTextSearchConfiguration.Default.Value;

    /// <summary>Gets or sets how many seconds a single database command may run before it is cancelled.</summary>
    /// <remarks>
    /// Configured rather than left at the provider's default so that the bound on a database command is a stated
    /// deployment decision, visible next to the connection settings, rather than whichever value the driver ships. It
    /// governs one command, not one unit of work: a session that issues several commands is bounded by the caller's
    /// cancellation token instead.
    /// </remarks>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds;

    /// <summary>Gets or sets the reference to a complete PostgreSQL connection string, or <see langword="null" /> when <c>ConnectionStrings:mailfathom</c> supplies it.</summary>
    /// <remarks>
    /// A connection string is more than a password, so a deployment backed by a secret store usually keeps it whole and
    /// rotates one artifact instead of splitting the credential across two systems. Configuring this replaces
    /// <c>ConnectionStrings:mailfathom</c> rather than adding to it.
    /// </remarks>
    public ConfiguredSecret? ConnectionString { get; set; }

    /// <summary>Gets or sets the reference to the PostgreSQL password, or <see langword="null" /> when the connection string already carries it.</summary>
    /// <remarks>
    /// The connection string keeps host, database, and user name; the password joins it only after resolution, so
    /// configuration never carries it. A block present with a blank reference fails startup rather than silently
    /// falling back to the unchanged connection string, because an operator who wrote the block meant to supply a
    /// password.
    /// </remarks>
    public ConfiguredSecret? Password { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PostgresTextSearchConfiguration.IsSupported(this.TextSearchConfiguration))
        {
            yield return new ValidationResult(
                $"'{this.TextSearchConfiguration}' is not a PostgreSQL text search configuration MailFathom supports. Supported configurations are: {string.Join(", ", PostgresTextSearchConfiguration.SupportedNames)}.",
                [nameof(this.TextSearchConfiguration)]);
        }
    }
}
