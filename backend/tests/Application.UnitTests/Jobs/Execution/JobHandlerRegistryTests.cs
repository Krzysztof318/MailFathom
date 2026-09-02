// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobHandlerRegistryTests
{
    /// <summary>The claim is filtered to these names, so a type missing from them is work this process never takes.</summary>
    [Fact]
    public void HandledTypes_AHandlerPerRegisteredType_NamesEachOfThem()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);

        // Act
        var registry = new JobHandlerRegistry([handler]);

        // Assert
        Assert.Equal([JobType.ClassifyEmailSpam], registry.HandledTypes);
    }

    /// <summary>A build whose consumers have not arrived registers none, and that is a state the worker acts on rather than a failure.</summary>
    [Fact]
    public void HandledTypes_NoRegisteredHandler_IsEmpty()
    {
        // Act
        var registry = new JobHandlerRegistry([]);

        // Assert
        Assert.Empty(registry.HandledTypes);
    }

    /// <summary>Dispatch is by type and by nothing else, which is what lets one worker run every kind of work.</summary>
    [Fact]
    public void TryGetHandler_ARegisteredType_AnswersWithTheHandlerForIt()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var registry = new JobHandlerRegistry([handler]);

        // Act
        var found = registry.TryGetHandler(JobType.ClassifyEmailSpam, out var dispatched);

        // Assert
        Assert.True(found);
        Assert.Same(handler, dispatched);
    }

    /// <summary>An unregistered type is the missing-handler path the executor records a failure for rather than retrying.</summary>
    [Fact]
    public void TryGetHandler_ATypeNoHandlerNames_AnswersWithNothing()
    {
        // Arrange
        var registry = new JobHandlerRegistry([]);

        // Act
        var found = registry.TryGetHandler(JobType.ClassifyEmailSpam, out var dispatched);

        // Assert
        Assert.False(found);
        Assert.Null(dispatched);
    }

    /// <summary>Either handler could be the one meant, so registration order must not be what decides which runs.</summary>
    [Fact]
    public void Constructor_TwoHandlersForOneType_IsRefused()
    {
        // Arrange
        var handlers = new[]
        {
            new RecordingJobHandler(JobType.ClassifyEmailSpam),
            new RecordingJobHandler(JobType.ClassifyEmailSpam),
        };

        // Act
        var exception = Assert.Throws<ArgumentException>(() => new JobHandlerRegistry(handlers));

        // Assert
        Assert.Contains(JobType.ClassifyEmailSpam.Name, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The struct default names no type, so a handler carrying it would filter the claim on nothing.</summary>
    [Fact]
    public void Constructor_AHandlerNamingTheUnspecifiedType_IsRefused()
    {
        // Arrange
        var handlers = new[] { new RecordingJobHandler(default) };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new JobHandlerRegistry(handlers));
    }
}
