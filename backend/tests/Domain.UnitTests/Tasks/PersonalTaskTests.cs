// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>Covers what a task may be built from, and the two things only a stored row may say about one.</summary>
public sealed class PersonalTaskTests
{
    private static readonly UserId User = UserId.Create(Guid.NewGuid());

    private static readonly PersonalTaskId Identifier = PersonalTaskId.Create(Guid.NewGuid());

    /// <summary>A task a person entered stands outstanding, asserted, and citing nothing.</summary>
    [Fact]
    public void Compose_ATaskAPersonEntered_StandsOutstandingAndAsserted()
    {
        // Arrange
        var dueOn = new DateOnly(2026, 9, 27);

        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Send the counter-proposal",
            dueOn,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Equal(Identifier, task.Id);
        Assert.Equal(User, task.User);
        Assert.Equal("Send the counter-proposal", task.Title);
        Assert.Equal(dueOn, task.DueOn);
        Assert.Equal(PersonalTaskOrigin.Asserted, task.Origin);
        Assert.Null(task.SourceMessage);
        Assert.False(task.IsCompleted);
    }

    /// <summary>A task read out of mail cites the message it was read from, whichever origin it carries.</summary>
    [Fact]
    public void Compose_ATaskReadOutOfMail_CitesTheMessageItWasReadFrom()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Confirm the delivery date",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Proposed,
            message);

        // Assert
        Assert.Equal(PersonalTaskOrigin.Proposed, task.Origin);
        Assert.Equal(message, task.SourceMessage);
    }

    /// <summary>Nobody has to say when, which is what leaves a task off every dated grouping until they do.</summary>
    [Fact]
    public void Compose_ATaskNobodyHasDated_CarriesNoDueDay()
    {
        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Close the budget",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Null(task.DueOn);
    }

    /// <summary>Surrounding whitespace is not part of the line a list is drawn with.</summary>
    [Fact]
    public void Compose_ATitleWrittenWithSurroundingWhitespace_KeepsOnlyTheLineItself()
    {
        // Act
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "  Sign the NDA \n",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Equal("Sign the NDA", task.Title);
    }

    /// <summary>A task is a line on a list, so a title past the stored bound is refused rather than truncated.</summary>
    [Fact]
    public void Compose_ATitlePastTheStoredBound_IsRefused()
    {
        // Arrange
        var title = new string('x', PersonalTask.MaximumTitleLength + 1);

        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            title,
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(composing);
    }

    /// <summary>A blank title names nothing a person could act on.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_ABlankTitle_IsRefused(string title)
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            title,
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Throws<ArgumentException>(composing);
    }

    /// <summary>A task held under the unspecified user would belong to nobody, so it is never built.</summary>
    [Fact]
    public void Compose_TheUnspecifiedUser_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            default,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Throws<ArgumentException>(composing);
    }

    /// <summary>An identifier that addresses nothing is reachable as the struct default and is refused here.</summary>
    [Fact]
    public void Compose_AnIdentifierAddressingNothing_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            default,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Throws<ArgumentException>(composing);
    }

    /// <summary>An origin this build does not declare says nothing about whether the person committed to the task.</summary>
    [Fact]
    public void Compose_AnUndeclaredOrigin_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            (PersonalTaskOrigin)42,
            sourceMessage: null);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(composing);
    }

    /// <summary>Completion is the store's to say, which is why restoring takes it and composing does not.</summary>
    [Fact]
    public void Restore_ATaskThePersonHasDone_ReadsBackAsCompleted()
    {
        // Act
        var task = PersonalTask.Restore(
            Identifier,
            User,
            "Sign the NDA",
            new DateOnly(2026, 9, 12),
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            isCompleted: true);

        // Assert
        Assert.True(task.IsCompleted);
    }

    /// <summary>A row read back is input from outside this process, so it is held to what composing is held to.</summary>
    [Fact]
    public void Restore_ATitlePastTheStoredBound_IsRefused()
    {
        // Arrange
        var title = new string('x', PersonalTask.MaximumTitleLength + 1);

        // Act
        var restoring = () => PersonalTask.Restore(
            Identifier,
            User,
            title,
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            isCompleted: false);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(restoring);
    }

    /// <summary>An edit is about the line and the day, and the rest of the record is not the editor's to state.</summary>
    [Fact]
    public void Revise_ATaskAPersonEdited_WritesTheLineAndTheDayAndKeepsEverythingElse()
    {
        // Arrange
        var cited = StoredEmailId.Create(Guid.NewGuid());
        var proposed = PersonalTask.Restore(
            Identifier,
            User,
            "Reply to the tender",
            new DateOnly(2026, 9, 27),
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Proposed,
            cited,
            isCompleted: true);

        // Act
        var revised = proposed.Revise("  Reply to the tender by Friday  ", dueOn: null, TaskAnnouncement.Silent);

        // Assert
        Assert.Equal("Reply to the tender by Friday", revised.Title);
        Assert.Null(revised.DueOn);
        Assert.Equal(Identifier, revised.Id);
        Assert.Equal(User, revised.User);
        Assert.Equal(PersonalTaskOrigin.Proposed, revised.Origin);
        Assert.Equal(cited, revised.SourceMessage);
        Assert.True(revised.IsCompleted);
    }

    /// <summary>The title is held to what composing holds it to, because an edit is where a person's own text arrives.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revise_ATitleStatingNothing_IsRefused(string title)
    {
        // Arrange
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Act
        var revising = () => task.Revise(title, dueOn: null, TaskAnnouncement.Silent);

        // Assert
        Assert.Throws<ArgumentException>(revising);
    }

    /// <summary>The stored bound holds however the text arrives, so an edit cannot write a title a row could not be restored from.</summary>
    [Fact]
    public void Revise_ATitlePastTheStoredBound_IsRefused()
    {
        // Arrange
        var task = PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Act
        var revising = () => task.Revise(new string('x', PersonalTask.MaximumTitleLength + 1), dueOn: null, TaskAnnouncement.Silent);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(revising);
    }
}
