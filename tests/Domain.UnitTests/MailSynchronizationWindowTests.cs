// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Synchronization;
using Xunit;

namespace MailFathom.Domain.UnitTests;

public sealed class MailSynchronizationWindowTests
{
    [Fact]
    public void Unbounded_NamesNoDate_ReachesEveryEmailTheFolderHolds()
    {
        // Arrange, Act
        var window = MailSynchronizationWindow.Unbounded;

        // Assert
        Assert.Null(window.EarliestEmailReceivedDate);
    }

    /// <summary>An account that configures no bound must read as unbounded rather than as a distinct third state.</summary>
    [Fact]
    public void Unbounded_ComparedWithADefaultValue_IsTheSameWindow()
    {
        // Arrange, Act
        var defaultWindow = default(MailSynchronizationWindow);

        // Assert
        Assert.Equal(MailSynchronizationWindow.Unbounded, defaultWindow);
    }

    [Fact]
    public void EmailsReceivedSince_ADate_CarriesItAsTheEarliestReceivedDate()
    {
        // Arrange
        var earliestReceivedDate = new DateOnly(2024, 1, 1);

        // Act
        var window = MailSynchronizationWindow.EmailsReceivedSince(earliestReceivedDate);

        // Assert
        Assert.Equal(earliestReceivedDate, window.EarliestEmailReceivedDate);
    }
}
