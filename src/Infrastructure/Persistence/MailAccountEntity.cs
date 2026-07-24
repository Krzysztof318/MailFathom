// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Infrastructure.Persistence;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[ExcludeFromCodeCoverage(Justification = "Provider-boundary adapter behavior requires future integration coverage.")]
internal sealed class MailAccountEntity
{
    public required string AccountId { get; set; }
}
