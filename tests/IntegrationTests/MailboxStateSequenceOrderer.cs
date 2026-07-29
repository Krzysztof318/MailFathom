// Copyright © 2026 Krzysztof Kasprowicz

using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace MailMcp.IntegrationTests;

/// <summary>Runs the tests of one class in the order their <see cref="MailboxStateStepAttribute" /> states.</summary>
/// <remarks>
/// <para>
/// Ordering is the exception this suite is allowed and the unit suites are not. A test class here shares one mailbox
/// and one database with every other test in the assembly, and some of what the specification asks for is a claim about
/// two runs rather than one: that synchronizing a folder again stores nothing twice. Expressed as a single test, the
/// second run's assertions would be reported as a failure of the first run's arrangement; expressed as two tests
/// without an order, whichever ran first would decide what the other saw.
/// </para>
/// <para>
/// The order is read from an attribute rather than from the method name, so renaming a test cannot silently reorder the
/// sequence, and it is read through reflection because that is the only shape xUnit's extensibility offers. A test
/// carrying no step runs after every test that carries one, which keeps a class that needs no sequence unaffected.
/// </para>
/// </remarks>
public sealed class MailboxStateSequenceOrderer : ITestCaseOrderer
{
    /// <inheritdoc />
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : notnull, ITestCase =>
    [
        .. testCases
            .OrderBy(testCase => ReadStep(testCase))
            .ThenBy(testCase => testCase.TestCaseDisplayName, StringComparer.Ordinal),
    ];

    private static int ReadStep(ITestCase testCase) => testCase.TestMethod is IXunitTestMethod testMethod
        ? testMethod.Method.GetCustomAttribute<MailboxStateStepAttribute>()?.Position ?? int.MaxValue
        : int.MaxValue;
}

/// <summary>Places one test within its class's mailbox-state sequence.</summary>
/// <param name="position">The position, ascending, with lower positions running first.</param>
/// <remarks>
/// Apply this only where a later test genuinely reads state an earlier one produced. Everywhere else the tests stay
/// order-independent, and adding a step to a test that does not need one hides that it could have run alone.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MailboxStateStepAttribute(int position) : Attribute
{
    /// <summary>Gets the position within the sequence.</summary>
    public int Position { get; } = position;
}
