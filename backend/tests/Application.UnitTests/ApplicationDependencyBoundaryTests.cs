// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using Xunit;

namespace MailFathom.Application.UnitTests;

/// <summary>Covers what the two inner boundaries are allowed to depend on.</summary>
/// <remarks>
/// The answering capability is the first thing MailFathom composes over an orchestration framework, and the port it is
/// reached through publishes a question and an answer rather than any of that framework's shapes. What guarantees it is
/// structural rather than reviewed: an assembly cannot name a type it does not reference, so asserting the reference
/// list proves it for every port added later as well as for the ones here now.
/// </remarks>
public sealed class ApplicationDependencyBoundaryTests
{
    /// <summary>
    /// An agent framework, a chat client library, a mail library, or a persistence provider arriving here would mean an
    /// application contract had started speaking one of their vocabularies. Each would have to become a reference
    /// first, which is what this refuses.
    /// </summary>
    [Theory]
    [InlineData("MailFathom.Application")]
    [InlineData("MailFathom.Domain")]
    public void InnerAssembly_ReferencesNothingButTheBoundariesInsideIt(string assemblyName)
    {
        // Arrange
        var assembly = Assembly.Load(assemblyName);

        // Act
        var referenced = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                && !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(
            assemblyName == "MailFathom.Application" ? ["MailFathom.Domain"] : [],
            referenced);
    }
}
