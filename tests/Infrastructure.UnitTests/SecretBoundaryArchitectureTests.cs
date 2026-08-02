// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections;
using System.Reflection;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

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

    [Theory]
    [InlineData("Password")]
    [InlineData("ClientSecret")]
    [InlineData("RefreshToken")]
    [InlineData("ApiKey")]
    [InlineData("PrivateKey")]
    public void NamesASecret_CredentialBearingName_IsRecognized(string propertyName)
    {
        // Arrange, Act, Assert
        Assert.True(SecretPropertyNaming.NamesASecret(propertyName));
    }

    /// <summary>
    /// An address locates a credential's issuer rather than holding one. OAuth's <c>TokenEndpoint</c> is the case
    /// that forces the distinction: it is the published name for the address a grant is exchanged at, every accurate
    /// name for it contains "token", and classifying it as a secret would make an operator provision a public URL as
    /// though it were a credential.
    /// </summary>
    [Theory]
    [InlineData("TokenEndpoint")]
    [InlineData("TokenUri")]
    [InlineData("CredentialServiceUrl")]
    public void NamesASecret_AddressOfWhereACredentialComesFrom_IsNotRecognizedAsOne(string propertyName)
    {
        // Arrange, Act, Assert
        Assert.False(SecretPropertyNaming.NamesASecret(propertyName));
    }

    [Fact]
    public void NamesASecret_NameThatOnlyStartsWithAnAddressWord_IsStillRecognizedAsASecret()
    {
        // Arrange, Act, Assert: the suffix is what makes a name an address, so this stays a secret.
        Assert.True(SecretPropertyNaming.NamesASecret("EndpointToken"));
        Assert.True(SecretPropertyNaming.NamesASecret("UrlSigningSecret"));
    }

    private static IReadOnlyList<string> FindRawSecretProperties(Type optionsType)
    {
        var visitedTypes = new HashSet<Type>();

        return FindRawSecretProperties(optionsType, optionsType.Name, visitedTypes);
    }

    // The cycle guard makes the walk stateful, so every level materializes instead of returning a deferred query. A
    // lazy pipeline over a mutating visitedTypes would yield whatever the set happened to hold when the caller
    // enumerated it, and a second enumeration would report nothing because every type is already marked visited.
    private static IReadOnlyList<string> FindRawSecretProperties(
        Type type,
        string path,
        HashSet<Type> visitedTypes)
    {
        if (type == typeof(ConfiguredSecret) || !visitedTypes.Add(type))
        {
            return [];
        }

        return
        [
            .. type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(property => FindRawSecretProperties(property, $"{path}.{property.Name}", visitedTypes)),
        ];
    }

    private static IReadOnlyList<string> FindRawSecretProperties(
        PropertyInfo property,
        string propertyPath,
        HashSet<Type> visitedTypes)
    {
        if (property.PropertyType != typeof(string))
        {
            return FindNestedRawSecretProperties(property.PropertyType, propertyPath, visitedTypes);
        }

        return SecretPropertyNaming.NamesASecret(property.Name) ? [propertyPath] : [];
    }

    private static IReadOnlyList<string> FindNestedRawSecretProperties(
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
