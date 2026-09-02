// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers where the drawing library the administrative command renders through is allowed to be spoken.</summary>
/// <remarks>
/// <para>
/// Root <c>AGENTS.md</c> keeps a third-party type inside the adapter that owns it, and this is the case where the rule
/// protects a property rather than a contract: what the command prints is decided in one place precisely so that a
/// listing is not laid out again in every command that reads one, which is the shape the hand-indented blocks under
/// each record used to be. A command file reaching a table, a style, or a console directly is how that returns, one
/// call site at a time and without anything failing.
/// </para>
/// <para>
/// It is a rule rather than a reference assertion because <c>Cli</c> genuinely references the package: nothing about
/// its reference set distinguishes the renderer from the commands beside it, and the boundary being asserted is a
/// namespace within one assembly, which is what this project exists to reach.
/// </para>
/// </remarks>
public sealed class CommandOutputBoundaryTests
{
    private const string RendererPattern = @"^MailFathom\.Cli\.Output\.";

    private const string DrawingLibraryPattern = @"^Spectre\.Console\.";

    [Fact]
    public void DrawingLibraryTypes_OutsideTheRenderer_AreUnreachable()
    {
        // Arrange
        IArchRule theDrawingLibraryStaysInsideTheRenderer = Types()
            .That()
            .DoNotHaveFullNameMatching(RendererPattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(DrawingLibraryPattern)
            .Because(
                "a command states what it means — a line, a caution, a failure, a listing, a record — and one place "
                    + "decides how that is drawn, so a table, a style, or a console reached from a command file would "
                    + "put the layout back into the commands this arrangement took it out of");

        // Act & Assert
        theDrawingLibraryStaysInsideTheRenderer.Check(CompiledBoundaries.Solution);
    }
}
