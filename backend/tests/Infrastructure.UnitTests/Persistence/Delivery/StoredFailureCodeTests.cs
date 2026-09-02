// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Delivery;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Delivery;

/// <summary>
/// Covers the one reading every table that records why an attempt stopped shares. The number a later build allocated is
/// the case worth stating: refusing such a row would cost the read of a record that is otherwise entirely readable, for
/// a value nothing acts on.
/// </summary>
public sealed class StoredFailureCodeTests
{
    /// <summary>A code this build has a member for is read back as that member.</summary>
    [Fact]
    public void ToErrorCode_ACodeThisBuildDeclares_ReadsItBack()
    {
        // Arrange
        var declared = MailFathomErrorCode.PersistenceConcurrencyConflict;

        // Act
        var code = StoredFailureCode.ToErrorCode(declared.Value);

        // Assert
        Assert.Equal(declared, code);
    }

    /// <summary>A column nothing was written into is the ordinary row of a record that never failed.</summary>
    [Fact]
    public void ToErrorCode_AnEmptyColumn_ReadsAsNoFailure()
    {
        // Act
        var code = StoredFailureCode.ToErrorCode(null);

        // Assert
        Assert.Null(code);
    }

    /// <summary>Version skew rather than corruption: a build that allocated a code since wrote the row.</summary>
    [Fact]
    public void ToErrorCode_ACodeThisBuildHasNotAllocated_ReadsAsNoFailure()
    {
        // Act
        var code = StoredFailureCode.ToErrorCode(99999);

        // Assert
        Assert.Null(code);
    }
}
