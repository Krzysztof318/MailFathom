// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections;
using System.Reflection;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Infrastructure.Secrets;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Keeps the secret-resolution boundary and the secret block marker enforced at build time.</summary>
public sealed class SecretBoundaryArchitectureTests
{
    [Fact]
    public void SecretResolutionTypes_DomainAndApplicationAssemblies_AreNotReachable()
    {
        // Arrange
        var secretAssemblyName = typeof(ISecretReferenceResolver).Assembly.GetName().Name;
        var boundaryAssemblies = new[] { typeof(MailAccountId).Assembly, typeof(MailboxSynchronizer).Assembly };

        // Act
        var referencedNames = boundaryAssemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name);

        // Assert
        Assert.DoesNotContain(secretAssemblyName, referencedNames);
    }

    /// <summary>
    /// Fails the build when an options type declares a secret as a raw string instead of a
    /// <see cref="ConfiguredSecret" /> block. The rule is name-based and cannot catch a secret called <c>Value</c>; it
    /// exists so the ordinary mistake fails rather than ships. Scanning the assembly rather than an inline list means an
    /// options type added later is covered without anyone remembering to register it.
    /// </summary>
    [Fact]
    public void InfrastructureOptionsTypes_PropertyNamedForASecret_BindsToConfiguredSecret()
    {
        // Arrange
        var optionsTypes = typeof(ISecretReferenceResolver).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Options", StringComparison.Ordinal));

        // Act
        var rawSecretProperties = optionsTypes
            .SelectMany(FindRawSecretProperties)
            .ToArray();

        // Assert
        Assert.Empty(rawSecretProperties);
    }

    private static IEnumerable<string> FindRawSecretProperties(Type optionsType)
    {
        var visitedTypes = new HashSet<Type>();

        return FindRawSecretProperties(optionsType, optionsType.Name, visitedTypes);
    }

    private static IEnumerable<string> FindRawSecretProperties(
        Type type,
        string path,
        HashSet<Type> visitedTypes)
    {
        if (type == typeof(ConfiguredSecret) || !visitedTypes.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyPath = $"{path}.{property.Name}";

            if (property.PropertyType == typeof(string))
            {
                if (SecretPropertyNaming.NamesASecret(property.Name))
                {
                    yield return propertyPath;
                }

                continue;
            }

            foreach (var nested in FindNestedRawSecretProperties(property.PropertyType, propertyPath, visitedTypes))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<string> FindNestedRawSecretProperties(
        Type propertyType,
        string propertyPath,
        HashSet<Type> visitedTypes)
    {
        var elementType = typeof(IEnumerable).IsAssignableFrom(propertyType)
            ? propertyType.GetGenericArguments().FirstOrDefault()
            : propertyType;

        return elementType is { IsClass: true } && IsOwnedType(elementType)
            ? FindRawSecretProperties(elementType, propertyPath, visitedTypes)
            : [];
    }

    private static bool IsOwnedType(Type type) => type.Assembly.GetName().Name?.StartsWith(
        ConfiguredSecretDiscovery.OwnedAssemblyNamePrefix,
        StringComparison.Ordinal) == true;
}
