// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Tasks;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Tasks;

/// <summary>Covers the row a task is written as, and the task a row is read back into.</summary>
public sealed class PersonalTaskMappingTests
{
    private static readonly MailUserId User = MailUserId.Create(Guid.NewGuid());

    private static readonly DateOnly DueOn = new(2026, 9, 27);

    /// <summary>Everything the task stated reaches the row, because nothing else re-derives any of it.</summary>
    [Fact]
    public void ToEntity_ATaskCitingAMessage_KeepsEveryPartOfIt()
    {
        // Arrange
        var identifier = PersonalTaskId.Create(Guid.NewGuid());
        var message = StoredEmailId.Create(Guid.NewGuid());
        var task = PersonalTask.Compose(
            identifier,
            User,
            "Send the counter-proposal",
            DueOn,
            PersonalTaskOrigin.Proposed,
            message);

        // Act
        var entity = PersonalTaskMapping.ToEntity(task);

        // Assert
        Assert.Equal(identifier.Value, entity.Id);
        Assert.Equal(User.Value, entity.UserId);
        Assert.Equal("Send the counter-proposal", entity.Title);
        Assert.Equal(DueOn, entity.DueOn);
        Assert.Equal(PersonalTaskOrigin.Proposed, entity.Origin);
        Assert.Equal(message.Value, entity.SourceStoredEmailId);
        Assert.False(entity.IsCompleted);
    }

    /// <summary>A task citing no message leaves the column empty rather than filling it with anything.</summary>
    [Fact]
    public void ToEntity_ATaskCitingNoMessage_LeavesTheCitationEmpty()
    {
        // Arrange
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.NewGuid()),
            User,
            "Close the budget",
            dueOn: null,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Act
        var entity = PersonalTaskMapping.ToEntity(task);

        // Assert
        Assert.Null(entity.SourceStoredEmailId);
        Assert.Null(entity.DueOn);
    }

    /// <summary>A row read back is the task it was written from, completion and citation included.</summary>
    [Fact]
    public void ToPersonalTask_ARowACompletedTaskWasWrittenAs_ReadsBackAsThatTask()
    {
        // Arrange
        var identifier = Guid.NewGuid();
        var message = Guid.NewGuid();
        var entity = new PersonalTaskEntity
        {
            Id = identifier,
            UserId = User.Value,
            Title = "Sign the NDA",
            DueOn = DueOn,
            Origin = PersonalTaskOrigin.Asserted,
            IsCompleted = true,
            SourceStoredEmailId = message,
        };

        // Act
        var task = PersonalTaskMapping.ToPersonalTask(entity);

        // Assert
        Assert.Equal(identifier, task.Id.Value);
        Assert.Equal(User, task.User);
        Assert.Equal("Sign the NDA", task.Title);
        Assert.Equal(DueOn, task.DueOn);
        Assert.Equal(PersonalTaskOrigin.Asserted, task.Origin);
        Assert.Equal(message, task.SourceMessage?.Value);
        Assert.True(task.IsCompleted);
    }

    /// <summary>A row citing no message reads back as a task that cites none, which most tasks are.</summary>
    [Fact]
    public void ToPersonalTask_ARowCitingNoMessage_ReadsBackAsATaskCitingNothing()
    {
        // Arrange
        var entity = new PersonalTaskEntity
        {
            Id = Guid.NewGuid(),
            UserId = User.Value,
            Title = "Sign the NDA",
            DueOn = null,
            Origin = PersonalTaskOrigin.Asserted,
            IsCompleted = false,
            SourceStoredEmailId = null,
        };

        // Act
        var task = PersonalTaskMapping.ToPersonalTask(entity);

        // Assert
        Assert.Null(task.SourceMessage);
    }
}
