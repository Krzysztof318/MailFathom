// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>Covers the position a continued walk of a person's task list reads beyond.</summary>
public sealed class PersonalTaskCursorTests
{
    /// <summary>Both halves of the position are kept, because the day alone does not separate two tasks sharing it.</summary>
    [Fact]
    public void After_ADatedPosition_KeepsTheDayAndTheTask()
    {
        // Arrange
        var task = PersonalTaskId.Create(Guid.NewGuid());
        var dueOn = new DateOnly(2026, 9, 27);

        // Act
        var cursor = PersonalTaskCursor.After(dueOn, task);

        // Assert
        Assert.Equal(dueOn, cursor.DueOn);
        Assert.Equal(task, cursor.Task);
    }

    /// <summary>
    /// A boundary with no day is the undated block the order puts last, which is a position like any other rather
    /// than an absent one.
    /// </summary>
    [Fact]
    public void After_AnUndatedPosition_IsAPositionRatherThanNoBoundary()
    {
        // Arrange
        var task = PersonalTaskId.Create(Guid.NewGuid());

        // Act
        var cursor = PersonalTaskCursor.After(dueOn: null, task);

        // Assert
        Assert.Null(cursor.DueOn);
        Assert.Equal(task, cursor.Task);
    }

    /// <summary>A boundary naming no task would repeat or skip every task sharing its day, so it is refused.</summary>
    [Fact]
    public void After_APositionNamingNoTask_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => PersonalTaskCursor.After(new DateOnly(2026, 9, 27), default));
    }
}
