// Copyright © 2026 Krzysztof Kasprowicz

using System.Reflection;
using Xunit;

namespace MailMcp.TestSupport;

/// <summary>Asserts that every exception an assembly declares takes part in one required exception hierarchy.</summary>
/// <remarks>
/// The required base type is a parameter rather than a fixed reference, so this file stays free of any production
/// dependency and can be compiled and tested on its own the way every other shared source is.
/// </remarks>
internal static class ExceptionHierarchyAssertion
{
    /// <summary>Asserts that no concrete exception the assembly publishes sits outside the required hierarchy.</summary>
    /// <param name="declaringAssembly">The production assembly to inspect.</param>
    /// <param name="requiredBaseType">The type every concrete published exception in the assembly must derive from.</param>
    /// <remarks>
    /// <para>
    /// A concrete exception outside the hierarchy is one no boundary can report by code and one whose message obeys no
    /// stated redaction contract. Reading the types reflectively is what makes the rule binding on an exception nobody
    /// has written yet, which is the only form in which the rule is worth having.
    /// </para>
    /// <para>
    /// Only types visible outside the assembly are inspected. An exception that stays internal is a control-flow signal
    /// between one implementation and its own caller: it reaches no boundary, so a published code would name something
    /// nothing publishes. `CA1064` already forces that choice, and an internal exception carries its suppression and the
    /// reason it does not escape.
    /// </para>
    /// </remarks>
    public static void AssertEveryDeclaredExceptionDerivesFrom(Assembly declaringAssembly, Type requiredBaseType)
    {
        ArgumentNullException.ThrowIfNull(declaringAssembly);
        ArgumentNullException.ThrowIfNull(requiredBaseType);

        string[] exceptionsOutsideTheHierarchy =
        [
            .. declaringAssembly.GetTypes()
                .Where(declaredType =>
                    declaredType.IsVisible &&
                    declaredType.IsSubclassOf(typeof(Exception)) &&
                    !declaredType.IsAbstract &&
                    !requiredBaseType.IsAssignableFrom(declaredType))
                .Select(declaredType => declaredType.FullName ?? declaredType.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            exceptionsOutsideTheHierarchy.Length is 0,
            $"{declaringAssembly.GetName().Name} declares exceptions outside {requiredBaseType.Name}: {string.Join(", ", exceptionsOutsideTheHierarchy)}.");
    }
}
