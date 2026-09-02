// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Application.Mail.Mutations;
using Xunit;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Covers what the protocol boundary is allowed to depend on.</summary>
/// <remarks>
/// The read-only tool set reaches no chat model, and the way that is guaranteed is structural rather than by review: an
/// assembly cannot call what it does not reference. Asserting the reference list is therefore the whole proof, and it
/// holds for every tool this project publishes now and for every one added later.
/// </remarks>
public sealed class McpDependencyBoundaryTests
{
    /// <summary>
    /// The retrieval a search performs is lexical because nothing else is reachable from here. An embedding client, a
    /// chat client, or a retrieval pipeline would have to arrive as a project or package reference first, which is what
    /// this assertion refuses — including the case where somebody adds one to answer a question a tool description
    /// already says the tool does not answer.
    /// </summary>
    /// <remarks>
    /// The list is an exact set rather than a prohibition, so a reference added for an ordinary reason lands here for
    /// review instead of passing unread. <c>MailFathom.Common</c> is on it because the protocol surface and the
    /// administrative command answer one question — where a version's documentation is — and Common's own reference
    /// set is Domain alone, so admitting it reaches nothing this assertion exists to keep out.
    /// </remarks>
    [Fact]
    public void McpAssembly_ReferencesNoAiBoundary()
    {
        // Arrange
        var mcpAssembly = Assembly.Load("MailFathom.Mcp");

        // Act
        var referencedMailFathomAssemblies = mcpAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.StartsWith("MailFathom.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal(
            ["MailFathom.Application", "MailFathom.Common", "MailFathom.Domain"],
            referencedMailFathomAssemblies);
    }

    /// <summary>No tool can obtain the one session able to change a mailbox, so no MCP request can mark mail read.</summary>
    /// <remarks>
    /// The reference list above cannot answer this one, because the write session is published by
    /// <c>MailFathom.Application</c> alongside everything the tools legitimately read. What separates them is the type a
    /// tool holds, and a tool that held the factory could set the remote <c>\Seen</c> flag while serving what a caller
    /// asked to be a read. The scan reaches method bodies as well as signatures: a local kept across an <c>await</c>
    /// and a captured variable both become fields of a compiler-generated type nested in the one that declared them.
    /// </remarks>
    [Fact]
    public void McpAssembly_HoldsNoTypeAbleToObtainAWriteSession()
    {
        // Arrange
        var mcpAssembly = Assembly.Load("MailFathom.Mcp");
        Type[] writeCapabilities = [typeof(IMailboxWriteSession), typeof(IMailboxWriteSessionFactory)];

        // Act
        var holders = mcpAssembly
            .GetTypes()
            .Where(type => MentionedTypes(type).Intersect(writeCapabilities).Any())
            .Select(type => type.FullName)
            .ToArray();

        // Assert
        Assert.Empty(holders);
    }

    /// <summary>Every type a member of <paramref name="type" /> names, with generic arguments unwrapped.</summary>
    private static IEnumerable<Type> MentionedTypes(Type type)
    {
        const BindingFlags Members = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(Members);
        var parameters = methods.SelectMany(method => method.GetParameters())
            .Concat(type.GetConstructors(Members).SelectMany(constructor => constructor.GetParameters()));

        return type.GetFields(Members).Select(field => field.FieldType)
            .Concat(type.GetProperties(Members).Select(property => property.PropertyType))
            .Concat(methods.Select(method => method.ReturnType))
            .Concat(parameters.Select(parameter => parameter.ParameterType))
            .SelectMany(Unwrap);
    }

    /// <summary>A type together with the types its generic arguments name, so a wrapped capability is not missed.</summary>
    private static IEnumerable<Type> Unwrap(Type type) => type.IsGenericType
        ? [type, .. type.GetGenericArguments().SelectMany(Unwrap)]
        : [type];
}
