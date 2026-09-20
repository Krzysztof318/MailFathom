// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.Domain.UnitTests.Calendar;

/// <summary>Covers the one value the identifier refuses, and what the default of the struct answers about itself.</summary>
public sealed class CalendarEventIdTests
{
    [Fact]
    public void Create_TheEmptyUuid_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => CalendarEventId.Create(Guid.Empty));

        // Assert
        Assert.Equal("value", refusal.ParamName);
    }

    [Fact]
    public void IsSpecified_TheDefaultOfTheStruct_NamesNoEvent()
    {
        // Act
        var unspecified = default(CalendarEventId);

        // Assert
        Assert.False(unspecified.IsSpecified);
    }

    [Fact]
    public void IsSpecified_ACreatedIdentifier_NamesAnEvent()
    {
        // Act
        var id = CalendarEventId.Create(Guid.CreateVersion7());

        // Assert
        Assert.True(id.IsSpecified);
    }
}
