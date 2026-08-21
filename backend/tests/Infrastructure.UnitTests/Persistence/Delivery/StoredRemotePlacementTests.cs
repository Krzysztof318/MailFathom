// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Delivery;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Delivery;

/// <summary>
/// Covers the one reading every table that records where a server put a copy shares. The half-written row is the case
/// worth stating: read as a placement it would name a UID with no UID space to interpret it in, which is what a later
/// removal would act on.
/// </summary>
public sealed class StoredRemotePlacementTests
{
    /// <summary>Both columns present is the server having named where the copy went.</summary>
    [Fact]
    public void Of_BothColumnsWritten_ReadsThePlacementTheServerNamed()
    {
        // Act
        var placement = StoredRemotePlacement.Of(uidValidity: 42u, uid: 7u);

        // Assert
        Assert.True(placement.IsReported);
        Assert.Equal(42u, placement.UidValidity?.Value);
        Assert.Equal(7u, placement.Uid?.Value);
    }

    /// <summary>The ordinary row: most servers answer an append without naming anything.</summary>
    [Fact]
    public void Of_NeitherColumnWritten_ReadsAsNothingReported()
    {
        // Act
        var placement = StoredRemotePlacement.Of(uidValidity: null, uid: null);

        // Assert
        Assert.False(placement.IsReported);
    }

    /// <summary>A row no writer here produces, and the one that must never become half a placement.</summary>
    [Theory]
    [InlineData(42u, null)]
    [InlineData(null, 7u)]
    public void Of_OneColumnWritten_ReadsAsNothingReported(uint? uidValidity, uint? uid)
    {
        // Act
        var placement = StoredRemotePlacement.Of(uidValidity, uid);

        // Assert
        Assert.False(placement.IsReported);
    }
}
