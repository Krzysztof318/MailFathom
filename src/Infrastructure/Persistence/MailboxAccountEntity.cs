// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class MailboxAccountEntity
{
    public required string Id { get; set; }

    public ICollection<MailFolderEntity> MailFolders { get; } = [];
}
