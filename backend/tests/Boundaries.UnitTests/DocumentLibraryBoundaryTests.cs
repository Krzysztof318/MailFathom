// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers where the library an attachment's text is read with is allowed to be spoken.</summary>
/// <remarks>
/// The same claim the mail-library rule beside it makes, about the parser with the worst input. An attachment is octets
/// a hostile sender fully controls, and the port above this adapter answers with characters and a closed set of
/// reasons — so a PdfPig document, page, or exception appearing anywhere else would mean somebody above the adapter had
/// been handed a parser's own model of a stranger's file to interpret, which is exactly what the port exists to stop.
/// A reference list cannot make that claim, because the library is referenced by this assembly legitimately; only a
/// rule about which namespace inside it may name the library can.
/// </remarks>
public sealed class DocumentLibraryBoundaryTests
{
    private const string DocumentAdapterPattern = @"^MailFathom\.Infrastructure\.Documents\.";

    private const string DocumentLibraryPattern = @"^UglyToad\.PdfPig\.";

    [Fact]
    public void DocumentLibraryTypes_OutsideTheDocumentAdapter_AreUnreachable()
    {
        // Arrange
        IArchRule theDocumentLibraryStaysInsideTheAdapter = Types()
            .That()
            .DoNotHaveFullNameMatching(DocumentAdapterPattern)
            .And()
            .DoNotHaveFullNameMatching(CompiledBoundaries.RegistrationSurfacePattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(DocumentLibraryPattern)
            .Because(
                "the attachment text extractor answers with characters and a closed set of reasons, so a PdfPig "
                    + "document, page, or exception reaching a boundary above would put a parser's reading of a "
                    + "hostile file into a contract that has to outlive it");

        // Act & Assert
        theDocumentLibraryStaysInsideTheAdapter.Check(CompiledBoundaries.Solution);
    }
}
