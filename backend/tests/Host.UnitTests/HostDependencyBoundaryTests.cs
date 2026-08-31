// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what the composition root is allowed to depend on, now that a deployment can serve the client from it.</summary>
/// <remarks>
/// The two stacks meet over HTTP and nowhere else, and the image serving the client's bundle is the one place that could
/// quietly stop being true: the bundle is copied into the published output as static files, so a reference added to
/// reach a type inside it would compile and would go unread. An assembly cannot serve what it does not reference, and it
/// cannot reference what is not in its graph, so the reference list is the whole proof.
/// </remarks>
public sealed class HostDependencyBoundaryTests
{
    /// <summary>
    /// The list is an exact set rather than a prohibition on one name, so a reference added for an ordinary reason
    /// lands here for review instead of passing unread. What it refuses above all is anything belonging to the client
    /// stack: nothing under <c>frontend/</c> enters <c>backend/MailFathom.slnx</c>, and a build of this one mentions
    /// none of it.
    /// </summary>
    [Fact]
    public void HostAssembly_ReferencesNoClientStackAssembly()
    {
        // Arrange
        var hostAssembly = Assembly.Load("MailFathom.Host");

        // Act
        var referencedMailFathomAssemblies = hostAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("MailFathom.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(
            [
                "MailFathom.AI",
                "MailFathom.Application",
                "MailFathom.Common",
                "MailFathom.Domain",
                "MailFathom.Infrastructure",
                "MailFathom.Mcp",
            ],
            referencedMailFathomAssemblies);
    }
}
