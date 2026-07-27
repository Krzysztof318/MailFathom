// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Host.Configuration;

/// <summary>Configures deployment-wide local persistence behavior.</summary>
/// <remarks>
/// Optimistic concurrency is bound once for the whole deployment rather than per use case, so every writer that
/// competes for the same PostgreSQL rows shares one operational limit.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class PersistenceOptions
{
    /// <summary>Gets or sets the maximum number of complete local write attempts after concurrency conflicts, including the first attempt.</summary>
    [Range(1, 10)]
    public int MaximumConcurrencyCommitAttempts { get; set; } = 2;

    /// <summary>Gets or sets the reference to a complete PostgreSQL connection string, or <see langword="null" /> when <c>ConnectionStrings:mailmcp</c> supplies it.</summary>
    /// <remarks>
    /// A connection string is more than a password, so a deployment backed by a secret store usually keeps it whole and
    /// rotates one artifact instead of splitting the credential across two systems. Configuring this replaces
    /// <c>ConnectionStrings:mailmcp</c> rather than adding to it.
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
}
