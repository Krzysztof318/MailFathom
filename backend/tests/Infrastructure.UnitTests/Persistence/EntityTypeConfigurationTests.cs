// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Keeps the model and the configurations beside the stores in step. Every table is mapped by an
/// <see cref="IEntityTypeConfiguration{TEntity}" /> that the context applies by name, so the two ways of forgetting
/// half of that pairing — an entity type the context exposes with no configuration written for it, and a configuration
/// written for a type the model does not hold — are what these assertions name. Both would otherwise surface as a
/// migration whose diff a reader has to interpret.
/// </summary>
public sealed class EntityTypeConfigurationTests
{
    private static readonly Type[] ConfigurationTypes =
    [
        .. typeof(MailFathomDbContext).Assembly
            .GetTypes()
            .Where(candidate => candidate is { IsClass: true, IsAbstract: false })
            .Where(candidate => candidate.GetInterfaces().Any(IsEntityTypeConfiguration)),
    ];

    [Fact]
    public void Model_EveryEntityType_IsMappedByAConfigurationOfItsOwn()
    {
        // Arrange
        var configured = ConfigurationTypes.Select(ConfiguredEntityType).ToHashSet();

        // Act
        string[] unconfigured =
        [
            .. ModelEntityTypes()
                .Where(entityType => !configured.Contains(entityType))
                .Select(entityType => entityType.Name),
        ];

        // Assert
        Assert.Empty(unconfigured);
    }

    [Fact]
    public void Configurations_EveryImplementation_MapsAnEntityTypeTheModelHolds()
    {
        // Arrange
        var mapped = ModelEntityTypes().ToHashSet();

        // Act
        string[] unapplied =
        [
            .. ConfigurationTypes
                .Where(configuration => !mapped.Contains(ConfiguredEntityType(configuration)))
                .Select(configuration => configuration.Name),
        ];

        // Assert
        Assert.Empty(unapplied);
    }

    /// <summary>
    /// Two configurations for one table would both be applied and the later one would silently amend the earlier, which
    /// is the one arrangement neither assertion above notices.
    /// </summary>
    [Fact]
    public void Configurations_EveryEntityType_IsMappedInOnePlaceOnly()
    {
        // Act
        string[] duplicated =
        [
            .. ConfigurationTypes
                .GroupBy(ConfiguredEntityType)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key.Name),
        ];

        // Assert
        Assert.Empty(duplicated);
    }

    private static bool IsEntityTypeConfiguration(Type candidate) =>
        candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>);

    private static Type ConfiguredEntityType(Type configuration) =>
        configuration.GetInterfaces().First(IsEntityTypeConfiguration).GetGenericArguments()[0];

    /// <summary>
    /// Reads the design-time model for the reason the index tests do: the runtime model is trimmed to what a query
    /// needs, and what the schema is generated from is this one.
    /// </summary>
    private static Type[] ModelEntityTypes()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return [.. context.Model.GetEntityTypes().Select(entityType => entityType.ClrType)];
    }
}
