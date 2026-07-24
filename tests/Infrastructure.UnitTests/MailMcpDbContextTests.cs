// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailMcpDbContextTests
{
    [Fact]
    public void Model_MessageMetadata_HasUniqueRemoteOccurrenceIndex()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<MailMcpDbContext>().UseNpgsql("Host=localhost;Database=mailmcp").Options;
        using var context = new MailMcpDbContext(options);

        // Act
        var entity = context.Model.FindEntityType("MailMcp.Infrastructure.Persistence.PostgreSql.MessageMetadataRecord");
        var index = entity?.GetIndexes().SingleOrDefault(i => string.Join(",", i.Properties.Select(p => p.Name)) == "AccountId,FolderName,UidValidity,Uid");

        // Assert
        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }
}
