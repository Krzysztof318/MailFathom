// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Application.Mail.Mutations;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations;

/// <summary>Covers which application types are able to change a remote mailbox at all.</summary>
/// <remarks>
/// <para>
/// The never-marks-mail-read guarantee is a property of the types rather than of a review, and this is the assertion
/// that keeps it one. Reading mail is what MailFathom does constantly and writing the <c>\Seen</c> flag is what it does
/// on an owner's instruction, so the two acts are separated by which session a component can obtain — and a change that
/// gave a synchronization pass, a reconciliation pass, or a content fetch that session would break the guarantee
/// silently, because everything it did afterwards would still compile and still pass its own tests.
/// </para>
/// <para>
/// The scan reaches method bodies as well as signatures. A local that lives across an <c>await</c> becomes a field of a
/// compiler-generated state machine and a captured variable becomes a field of a display class, so attributing every
/// nested type's mentions to the type that declares it catches a component that resolves the factory from a service
/// provider and never names it in a signature.
/// </para>
/// </remarks>
public sealed class MailboxWriteCapabilityBoundaryTests
{
    /// <summary>Exactly one application type can obtain a session that writes, and it is the one mutations go through.</summary>
    /// <remarks>
    /// Failing here is not a reason to extend the expected set. A read path that needs to write is a read path that has
    /// acquired something
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// refuses it, and the change belongs behind <see cref="IMailboxMutationPerformer" /> instead.
    /// </remarks>
    [Fact]
    public void ApplicationAssembly_HoldsTheWriteCapabilityInTheMutationPerformerAlone()
    {
        // Arrange
        var applicationAssembly = Assembly.Load("MailFathom.Application");
        Type[] writeCapabilities = [typeof(IMailboxWriteSession), typeof(IMailboxWriteSessionFactory)];

        // Act
        var holders = applicationAssembly
            .GetTypes()
            .Where(type => MentionedTypes(type).Intersect(writeCapabilities).Any())
            .Select(DeclaringRootOf)
            .Except(writeCapabilities)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Equal([nameof(MailboxMutationPerformer)], holders);
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

        var signatureTypes = type.GetFields(Members).Select(field => field.FieldType)
            .Concat(type.GetProperties(Members).Select(property => property.PropertyType))
            .Concat(methods.Select(method => method.ReturnType))
            .Concat(parameters.Select(parameter => parameter.ParameterType));

        return signatureTypes.SelectMany(Unwrap);
    }

    /// <summary>A type together with the types its generic arguments name, so a wrapped capability is not missed.</summary>
    private static IEnumerable<Type> Unwrap(Type type) => type.IsGenericType
        ? [type, .. type.GetGenericArguments().SelectMany(Unwrap)]
        : [type];

    /// <summary>The outermost type a nested or compiler-generated type was declared inside.</summary>
    private static Type DeclaringRootOf(Type type)
    {
        var root = type;

        while (root.DeclaringType is { } declaringType)
        {
            root = declaringType;
        }

        return root;
    }
}
