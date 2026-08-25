// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape the persisted configuration layer is declared with. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so what this states is the declaration a schema is generated from —
/// which is where each of these has to be true, because each is a guarantee the database gives rather than one the
/// host keeps.
/// </summary>
public sealed class RootSettingsModelTests
{
    /// <summary>One row, keyed by the constant the schema also refuses anything but.</summary>
    [Fact]
    public void RootSettingsModel_Key_IsTheSingletonTheSchemaEnforces()
    {
        // Arrange
        using var context = CreateContext();
        var entityType = EntityTypeOf<RootSettingsEntity>(context);

        // Act
        var key = entityType.FindPrimaryKey();
        var singleton = Assert.Single(entityType.GetCheckConstraints());

        // Assert
        Assert.Equal("settings_root", entityType.GetTableName());
        Assert.NotNull(key);
        Assert.Equal(["Id"], key.Properties.Select(property => property.Name));
        Assert.Equal(ValueGenerated.Never, key.Properties[0].ValueGenerated);
        Assert.Equal(PersistenceConstraintNames.RootSettingsSingletonCheckConstraintName, singleton.Name);
        Assert.Equal($"\"Id\" = {RootSettingsEntity.SingletonId}", singleton.Sql);
    }

    /// <summary>The persisted settings are one document, and the schema says nothing about what is in it.</summary>
    [Fact]
    public void RootSettingsModel_Document_IsARequiredJsonbDocument()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var document = EntityTypeOf<RootSettingsEntity>(context).FindProperty("Document");

        // Assert
        Assert.NotNull(document);
        Assert.Equal("jsonb", document.GetColumnType());
        Assert.False(document.IsNullable);
    }

    /// <summary>
    /// The version is the document's own rather than PostgreSQL's <c>xmin</c>, because a writer has to be able to state
    /// the version it read, be refused by number, and report the version it was refused against — none of which a token
    /// the database generates behind the write can answer.
    /// </summary>
    [Fact]
    public void RootSettingsModel_Version_IsAWrittenNumberRatherThanTheRowVersion()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var version = EntityTypeOf<RootSettingsEntity>(context).FindProperty("Version");

        // Assert
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, version.ValueGenerated);
    }

    /// <summary>
    /// Reads the design-time model rather than <c>DbContext.Model</c>, because the runtime model is trimmed to what a
    /// query needs and throws for the configuration a schema is generated from.
    /// </summary>
    private static IEntityType EntityTypeOf<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
