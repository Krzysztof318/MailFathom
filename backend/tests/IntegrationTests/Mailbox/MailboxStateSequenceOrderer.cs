// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace MailFathom.IntegrationTests.Mailbox;

/// <summary>Runs the tests of one class in the order their <see cref="MailboxStateStepAttribute" /> states.</summary>
/// <remarks>
/// <para>
/// Ordering is the exception this suite is allowed and the unit suites are not. A test class here shares one mailbox
/// and one database with every other test in the assembly, and some of what synchronization has to guarantee is a claim about
/// two runs rather than one: that synchronizing a folder again stores nothing twice. Expressed as a single test, the
/// second run's assertions would be reported as a failure of the first run's arrangement; expressed as two tests
/// without an order, whichever ran first would decide what the other saw.
/// </para>
/// <para>
/// The order is read from an attribute rather than from the method name, so renaming a test cannot silently reorder the
/// sequence, and it is read through reflection because that is the only shape xUnit's extensibility offers. A test
/// carrying no step runs after every test that carries one, which keeps a class that needs no sequence unaffected.
/// </para>
/// <para>
/// It orders methods rather than test cases, and the difference is the whole of whether it does anything. xUnit orders
/// a class in two passes — the methods of the class, then the cases of each method — and a test case orderer is handed
/// one method's cases at a time: for a fact that is a single case, so an orderer written against cases is asked to
/// sort a list of one and the class runs in whatever order the methods were discovered in.
/// </para>
/// </remarks>
public sealed class MailboxStateSequenceOrderer : ITestMethodOrderer
{
    /// <inheritdoc />
    /// <remarks>A test case that belongs to no method arrives as <see langword="null" />, and is run after everything that carries a step, as a method without one is.</remarks>
    public IReadOnlyCollection<TTestMethod?> OrderTestMethods<TTestMethod>(IReadOnlyCollection<TTestMethod?> testMethods)
        where TTestMethod : ITestMethod =>
    [
        .. testMethods
            .OrderBy(testMethod => ReadStep(testMethod))
            .ThenBy(testMethod => testMethod?.MethodName, StringComparer.Ordinal),
    ];

    private static int ReadStep(ITestMethod? testMethod) => testMethod is IXunitTestMethod xunitTestMethod
        ? xunitTestMethod.Method.GetCustomAttribute<MailboxStateStepAttribute>()?.Position ?? int.MaxValue
        : int.MaxValue;
}
