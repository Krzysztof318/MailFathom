// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace MailFathom.Boundaries.UnitTests;

/// <summary>Covers where the mail library is allowed to be spoken.</summary>
/// <remarks>
/// Root <c>AGENTS.md</c> keeps a third-party type inside the adapter that owns it, and MailKit is the case that
/// decides most: a message, a folder, and a summary from it are the shapes every boundary above would otherwise start
/// passing around, and a mail-library type reaching one of them is how the read-only guarantee stops being auditable
/// in one place.
/// </remarks>
public sealed class MailLibraryBoundaryTests
{
    private const string MailAdapterPattern = @"^MailFathom\.Infrastructure\.Mail\.";

    private const string MailLibraryPattern = @"^(MailKit|MimeKit)\.";

    /// <summary>
    /// The one type outside the adapter that reads a mail-library type, and it reads exactly one thing from it:
    /// whether a failure the protocol reported is transient. Sorting that is what the retry policy above every
    /// outbound call is decided from, and the classifier can only do it by naming the exceptions MailKit throws.
    /// </summary>
    private const string FailureClassifierPattern = @"^MailFathom\.Infrastructure\.Resilience\.TransientFailureClassifier";

    [Fact]
    public void MailLibraryTypes_OutsideTheMailAdapter_AreUnreachable()
    {
        // Arrange
        IArchRule theMailLibraryStaysInsideTheAdapter = Types()
            .That()
            .DoNotHaveFullNameMatching(MailAdapterPattern)
            .And()
            .DoNotHaveFullNameMatching(FailureClassifierPattern)
            .And()
            .DoNotHaveFullNameMatching(CompiledBoundaries.RegistrationSurfacePattern)
            .Should()
            .NotDependOnAnyTypesThat()
            .HaveFullNameMatching(MailLibraryPattern)
            .Because(
                "everything above the transport speaks the domain's own mail types, so a MailKit folder, message, or "
                    + "summary appearing elsewhere would put the protocol's vocabulary into a contract that has to "
                    + "outlive it");

        // Act & Assert
        theMailLibraryStaysInsideTheAdapter.Check(CompiledBoundaries.Solution);
    }
}
