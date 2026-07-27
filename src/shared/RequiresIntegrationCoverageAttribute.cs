// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.CodeCoverage;

/// <summary>
/// Marks production code whose meaningful verification requires real infrastructure or cross-component behavior,
/// so the unit-test coverage gate leaves it out of the measured denominator.
/// </summary>
/// <remarks>
/// The unit-test coverage collector is configured to drop every element carrying this attribute, and integration
/// tests never collect coverage, so marked code is measured by neither run. Apply it only where a unit test could
/// not prove anything a real database, mail server, or composed host proves; business logic stays unmarked and
/// subject to the configured minimum. It differs from <see cref="System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute" />,
/// which states that code should never participate in coverage at all, whereas this attribute states that the
/// verification exists elsewhere.
/// <para>
/// The attribute is compiled into each assembly that needs it from <c>src/shared/</c>, because the coverage
/// collector matches it by name rather than by declaring assembly.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Method |
    AttributeTargets.Constructor |
    AttributeTargets.Property,
    Inherited = false)]
internal sealed class RequiresIntegrationCoverageAttribute : Attribute;
