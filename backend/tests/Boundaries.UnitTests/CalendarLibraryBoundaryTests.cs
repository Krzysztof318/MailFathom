// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers where the library an offered calendar file is read with is allowed to be spoken.</summary>
/// <remarks>
/// The same claim the mail-library and document-library rules beside it make, about the third parser of a file somebody
/// else wrote. An <c>.ics</c> file is octets whoever handed it over fully controls, and the port above this adapter
/// answers with MailFathom's own entries and a closed set of reasons — so an Ical.Net calendar, entry, or date
/// appearing anywhere else would mean somebody above the adapter had been handed a parser's own model of a stranger's
/// file to interpret. <c>THIRD_PARTY_LICENSES.md</c> states that confinement as a fact about what MailFathom compiles
/// against, and this is what holds it to it. A reference list cannot make the claim, because the library is referenced
/// by this assembly legitimately; only a rule about which namespace inside it may name the library can.
/// </remarks>
public sealed class CalendarLibraryBoundaryTests
{
    private const string CalendarAdapterPattern = @"^MailFathom\.Infrastructure\.Calendar\.";

    private const string CalendarLibraryPattern = @"^Ical\.Net\.";

    /// <summary>
    /// The rule itself, which admits nothing: the registration surface the two rules beside this one have to exclude
    /// is not excluded here, because the composition names the reader by MailFathom's own type and the library by
    /// nothing at all.
    /// </summary>
    [Fact]
    public void CalendarLibraryTypes_OutsideTheCalendarAdapter_AreUnreachable()
    {
        // Arrange
        IArchRule theCalendarLibraryStaysInsideTheAdapter = Types()
            .That()
            .DoNotHaveFullNameMatching(CalendarAdapterPattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(CalendarLibraryPattern)
            .Because(
                "the calendar file reader answers with MailFathom's own entries and a closed set of reasons, so an "
                    + "Ical.Net calendar, entry, or date reaching a boundary above would put a parser's reading of a "
                    + "file somebody else wrote into a contract that has to outlive it");

        // Act & Assert
        theCalendarLibraryStaysInsideTheAdapter.Check(CompiledBoundaries.Solution);
    }
}
