// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;

namespace MailFathom.Client.UnitTests.Presentation.Messages;

/// <summary>What one run remembers of the places it read mail in, and what it drops to stay bounded.</summary>
public sealed class VisitedPlacesTests
{
    /// <summary>Leaving a folder and coming back to it is coming back, which is the whole of what this holds.</summary>
    [Fact]
    public void Read_APlaceThisRunHasBeenIn_ReadsBackWhereItWasLeft()
    {
        // Arrange
        var visited = new VisitedPlaces();
        var left = At("workINBOX", "after-the-third-page");

        // Act
        visited.Keep(left);

        // Assert
        Assert.Equal(left, visited.Read("workINBOX"));
    }

    /// <summary>A place this run has not been in is remembered as nothing rather than as somewhere else.</summary>
    [Fact]
    public void Read_APlaceThisRunHasNotBeenIn_RemembersNothing()
    {
        // Arrange
        var visited = new VisitedPlaces();
        visited.Keep(At("workINBOX", "after-the-third-page"));

        // Act, Assert
        Assert.Null(visited.Read("workArchive"));
    }

    /// <summary>Reading a folder again writes over where it was, because the newer position is where somebody now is.</summary>
    [Fact]
    public void Keep_APlaceWrittenAgain_ReadsBackAsTheNewerPosition()
    {
        // Arrange
        var visited = new VisitedPlaces();
        visited.Keep(At("workINBOX", "after-the-third-page"));

        // Act
        visited.Keep(At("workINBOX", "after-the-ninth-page"));

        // Assert
        Assert.Equal("after-the-ninth-page", visited.Read("workINBOX")?.Cursor);
        Assert.Equal(1, visited.Count);
    }

    /// <summary>
    /// A run has no bound of its own — a deeply nested mailbox has as many places as it has folders — so this one does,
    /// and what it drops is the place written longest ago.
    /// </summary>
    [Fact]
    public void Keep_MorePlacesThanTheBound_DropsTheOneWrittenLongestAgo()
    {
        // Arrange
        var visited = new VisitedPlaces();

        // Act
        for (var folder = 0; folder <= VisitedPlaces.Maximum; folder++)
        {
            visited.Keep(At($"workFolder-{folder}", $"after-{folder}"));
        }

        // Assert
        Assert.Equal(VisitedPlaces.Maximum, visited.Count);
        Assert.Null(visited.Read("workFolder-0"));
        Assert.NotNull(visited.Read("workFolder-1"));
        Assert.NotNull(visited.Read($"workFolder-{VisitedPlaces.Maximum}"));
    }

    /// <summary>
    /// The folder somebody keeps returning to is never the one dropped to make room for one they passed through once,
    /// which is what makes the bound safe to have at all.
    /// </summary>
    [Fact]
    public void Keep_APlaceReturnedTo_MovesToTheNewestEndRatherThanStayingWhereItFirstAppeared()
    {
        // Arrange
        var visited = new VisitedPlaces();
        visited.Keep(At("workINBOX", "after-the-third-page"));

        for (var folder = 1; folder < VisitedPlaces.Maximum; folder++)
        {
            visited.Keep(At($"workFolder-{folder}", $"after-{folder}"));
        }

        // Act
        visited.Keep(At("workINBOX", "after-the-ninth-page"));
        visited.Keep(At("workOne-More", "after-one-more"));

        // Assert
        Assert.Equal("after-the-ninth-page", visited.Read("workINBOX")?.Cursor);
        Assert.Null(visited.Read("workFolder-1"));
    }

    /// <summary>A place with no name to be remembered under would be one nothing could ever read back.</summary>
    [Fact]
    public void Read_NoPlaceName_IsRefused()
    {
        // Arrange
        var visited = new VisitedPlaces();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => visited.Read(null!));
        Assert.Throws<ArgumentException>(() => visited.Read(string.Empty));
        Assert.Throws<ArgumentNullException>(() => visited.Keep(null!));
    }

    private static RememberedMessageList At(string placeKey, string cursor) =>
        new(placeKey, cursor, MailTimelinePageDirection.Forward, MessageListArrangement.Default);
}
