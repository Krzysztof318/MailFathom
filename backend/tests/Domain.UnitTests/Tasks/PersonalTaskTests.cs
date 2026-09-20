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
    private static readonly MailUserId User = MailUserId.Create(Guid.NewGuid());

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
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            isCompleted: false);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(restoring);
    }
}
