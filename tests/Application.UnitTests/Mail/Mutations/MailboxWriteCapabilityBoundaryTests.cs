// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
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
/// <para>
/// The expected set is the list of tiers the decision record names and nothing else, so growing it by a *tier* is an
/// amendment to that record rather than an edit here. It has grown once that way: filing a copy of a message MailFathom
/// composed is a write no mutation of the owner's own mail can express, because the message it appends does not exist
/// on the server yet. It has grown once more without a tier being added — a draft's copies are filed by a type of their
/// own, under the same tier, which that record decided in advance when it said the drafts role is decided by the filing
/// tier and filed by nothing yet.
/// </para>
/// </remarks>
public sealed class MailboxWriteCapabilityBoundaryTests
{
    /// <summary>Three application types can obtain a session that writes, and each acts within a tier the decision record names.</summary>
    /// <remarks>
    /// Failing here is not a reason to extend the expected set. A read path that needs to write is a read path that has
    /// acquired something
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// refuses it, and a change to the owner's own mail belongs behind <see cref="IMailboxMutationPerformer" /> instead.
    /// A further name is admissible only where the act it performs is one that record already decided — which is what
    /// the draft filer is, since the copies it appends and withdraws are messages MailFathom composed and stored itself,
    /// in the folder the drafts role names, carrying the flag that tier assigns. A name performing anything else is a
    /// tier that record reopens for or refuses.
    /// </remarks>
    [Fact]
    public void ApplicationAssembly_HoldsTheWriteCapabilityInTheTiersTheDecisionRecordNames()
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
        Assert.Equal(
            [nameof(MailDraftFiler), nameof(MailboxMutationPerformer), nameof(OutgoingMailFiler)],
            holders);
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
