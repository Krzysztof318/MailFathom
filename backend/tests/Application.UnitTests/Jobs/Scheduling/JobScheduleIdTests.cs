// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Scheduling;

/// <summary>The identity a schedule's durable state is keyed by, and what it refuses to be composed of.</summary>
public sealed class JobScheduleIdTests
{
    /// <summary>Two readings of one declaration have to key the same row, or the schedule forgets what it dispatched.</summary>
    [Fact]
    public void Create_TheSameComposedText_ProducesEqualIdentities()
    {
        // Act
        var identity = JobScheduleId.Create("mail-rules:work:housekeeping");
        var sameIdentity = JobScheduleId.Create("  mail-rules:work:housekeeping  ");

        // Assert
        Assert.Equal(identity, sameIdentity);
        Assert.Equal("mail-rules:work:housekeeping", identity.Value);
        Assert.Equal("mail-rules:work:housekeeping", identity.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => JobScheduleId.Create(value));
    }

    [Fact]
    public void Create_NoText_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => JobScheduleId.Create(null!));
    }

    /// <summary>The bound is the primary key column's, so an identity over it would be refused by the database instead.</summary>
    [Fact]
    public void Create_TextLongerThanTheBound_IsRefused()
    {
        // Arrange
        var overLongIdentity = new string('s', JobScheduleId.MaximumLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => JobScheduleId.Create(overLongIdentity));
    }

    [Fact]
    public void Create_TextExactlyAtTheBound_IsAccepted()
    {
        // Arrange
        var longestIdentity = new string('s', JobScheduleId.MaximumLength);

        // Act
        var identity = JobScheduleId.Create(longestIdentity);

        // Assert
        Assert.Equal(longestIdentity, identity.Value);
    }

    /// <summary>An operator reads this text when they ask why a scheduled run has not happened.</summary>
    [Fact]
    public void Create_TextCarryingAControlCharacter_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => JobScheduleId.Create("mail-rules:work\n:housekeeping"));
    }
}
