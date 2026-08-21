// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers what the persistence adapter keeps to itself: its provider libraries and its entities.</summary>
/// <remarks>
/// Both rules are about a namespace rather than an assembly, which is what puts them out of reach of a reference list:
/// <c>Infrastructure</c> references Entity Framework Core whichever of its adapters uses it. Root <c>AGENTS.md</c>
/// requires PostgreSQL, Npgsql, and <c>bytea</c> details to stay inside the content-store adapter so that a later
/// object-store implementation changes no use case, and requires that no Entity Framework Core entity cross an
/// application boundary. A type's full name begins with its namespace, and a nested type's begins with the full name
/// of the type declaring it, so matching on the full name selects a namespace together with the compiler-generated
/// classes a lambda inside it becomes.
/// </remarks>
public sealed class PersistenceAdapterBoundaryTests
{
    private const string PersistenceAdapterPattern = @"^MailFathom\.Infrastructure\.Persistence\.";

    private const string EntityPattern = @"^MailFathom\.Infrastructure\.Persistence\.Entities\.";

    private const string ProviderPattern = @"^(Microsoft\.EntityFrameworkCore|Npgsql|Pgvector)\.";

    [Fact]
    public void PersistenceProviderTypes_OutsideThePersistenceAdapter_AreUnreachable()
    {
        // Arrange
        IArchRule theProviderStaysInsideTheAdapter = Types()
            .That()
            .DoNotHaveFullNameMatching(PersistenceAdapterPattern)
            .And()
            .DoNotHaveFullNameMatching(CompiledBoundaries.RegistrationSurfacePattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(ProviderPattern)
            .Because(
                "Entity Framework Core, Npgsql, and pgvector are how this adapter stores mail rather than how "
                    + "MailFathom describes it, so replacing the content store has to be a change inside these "
                    + "namespaces and nowhere else");

        // Act & Assert
        theProviderStaysInsideTheAdapter.Check(CompiledBoundaries.Solution);
    }

    [Fact]
    public void PersistenceEntities_OutsideThePersistenceAdapter_AreUnreachable()
    {
        // Arrange
        IArchRule entitiesStayInsideTheAdapter = Types()
            .That()
            .DoNotHaveFullNameMatching(PersistenceAdapterPattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(EntityPattern)
            .Because(
                "an entity is a row this adapter maps rather than a value another boundary reads, so one reaching a "
                    + "use case or the composition root would make the storage shape part of that contract");

        // Act & Assert
        entitiesStayInsideTheAdapter.Check(CompiledBoundaries.Solution);
    }
}
