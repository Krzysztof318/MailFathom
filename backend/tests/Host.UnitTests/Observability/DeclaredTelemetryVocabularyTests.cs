// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

/// <summary>Holds every telemetry name this deployment declares, in every assembly of it, against the contract.</summary>
/// <remarks>
/// <para>
/// The per-assembly suites assert the contract over what a drive emitted, which is the strongest form of it and also
/// the one with a reach: a name only a failure path sets, or one a hosted worker opens a span under, is declared and
/// never seen by a listener a unit test opened. This reads the declarations instead, so a name is covered by being
/// written rather than by being exercised.
/// </para>
/// <para>
/// It runs here because the composition root is the one project that references every other, so one test covers the
/// whole deployment rather than one per boundary. The assemblies are read off the host's own references rather than
/// listed, so a project added later is covered without this file being edited — and the host's own
/// <c>backfill_email_extraction</c> is the name that made the reach worth having, since nothing else in the repository
/// publishes a span from a worker.
/// </para>
/// </remarks>
public sealed class DeclaredTelemetryVocabularyTests
{
    /// <summary>No span name and no dimension key anywhere is named after mail, a person, or a secret.</summary>
    [Theory]
    [MemberData(nameof(ProductionAssemblyNames))]
    public void EveryDeclaredName_InEveryAssemblyOfThisDeployment_ObeysTheRedactionContract(string assemblyName) =>
        TelemetryRedactionContract.AssertEveryDeclaredNameObeysTheContract(
            Assembly.Load(new AssemblyName(assemblyName)));

    /// <summary>The reader finds the names, so the theory above is holding something against the contract.</summary>
    /// <remarks>
    /// The control for an assertion that is otherwise an absence. An assembly whose constants this failed to read
    /// reports no offending name in exactly the way one with nothing wrong in it does, so the two landmarks below are
    /// read back: the worker's span, which no listener in a unit test reaches and which is the reason this test exists,
    /// and one dimension, which establishes that the other half of the convention is read too.
    /// </remarks>
    [Fact]
    public void DeclaredTelemetryNames_AcrossThisDeployment_IncludeTheNamesOnlyADeclarationCarries()
    {
        // Arrange
        var declared = MailFathomAssemblyNames()
            .SelectMany(name => TelemetryRedactionContract.DeclaredTelemetryNamesIn(
                Assembly.Load(new AssemblyName(name))))
            .ToArray();

        // Act

        // Assert
        Assert.Contains(("backfill_email_extraction", true), declared);
        Assert.Contains(("mailfathom.mail.sync.outcome", false), declared);
    }

    /// <summary>Names every MailFathom assembly this host is composed of, itself included.</summary>
    /// <remarks>
    /// The names are the data rather than the assemblies themselves, because a theory's arguments are what xUnit names
    /// the case after and an assembly renders as a path nobody reads. It is a theory rather than one test over all of
    /// them so that a failure says which boundary declared the name.
    /// </remarks>
    public static TheoryData<string> ProductionAssemblyNames() => [.. MailFathomAssemblyNames()];

    /// <summary>Reads the host's own references for the assemblies this deployment is built from.</summary>
    private static IReadOnlyList<string> MailFathomAssemblyNames()
    {
        var host = typeof(ServiceDefaultsExtensions).Assembly;

        return
        [
            .. host.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null
                    && name.StartsWith("MailFathom.", StringComparison.Ordinal))
                .Select(name => name!)
                .Append(host.GetName().Name!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }
}
