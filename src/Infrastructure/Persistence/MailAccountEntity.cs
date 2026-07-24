// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Infrastructure.Persistence;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
internal sealed class MailAccountEntity
{
    public required string AccountId { get; set; }
}
