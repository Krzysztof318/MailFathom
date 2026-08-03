// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

public sealed class MailSynchronizationWindowValidatorTests
{
    /// <summary>A bound the clock has not reached excludes the whole mailbox, so startup must stop rather than run empty.</summary>
    [Fact]
    public void Validate_EarliestEmailReceivedDateInTheFuture_FailsWithAMessageNamingTheAccount()
    {
        // Arrange
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var validator = new MailSynchronizationWindowValidator(clock);
        var options = CreateOptions("primary", new DateOnly(2026, 7, 25));

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Account 'primary'", Assert.Single(result.Failures), StringComparison.Ordinal);
    }

    /// <summary>The comparison is made in UTC, so the current UTC date itself is still a usable bound.</summary>
    [Fact]
    public void Validate_EarliestEmailReceivedDateEqualToTheCurrentUtcDate_Succeeds()
    {
        // Arrange
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 23, 30, 0, TimeSpan.Zero));
        var validator = new MailSynchronizationWindowValidator(clock);
        var options = CreateOptions("primary", new DateOnly(2026, 7, 24));

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NoAccountBoundingHowFarBackToReach_Succeeds()
    {
        // Arrange
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var validator = new MailSynchronizationWindowValidator(clock);
        var options = CreateOptions("primary", earliestEmailReceivedDate: null);

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        Assert.True(result.Succeeded);
    }

    /// <summary>Every offending account is reported at once, so an operator fixes one snapshot rather than one account per restart.</summary>
    [Fact]
    public void Validate_SeveralAccountsBoundedInTheFuture_ReportsEachOfThem()
    {
        // Arrange
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var validator = new MailSynchronizationWindowValidator(clock);
        var options = new MailSynchronizationOptions
        {
            Accounts =
            [
                CreateAccount("primary", new DateOnly(2027, 1, 1)),
                CreateAccount("secondary", new DateOnly(2026, 12, 1)),
            ],
        };

        // Act
        var result = validator.Validate(name: null, options);

        // Assert
        Assert.Equal(2, result.Failures!.Count());
    }

    private static MailSynchronizationOptions CreateOptions(string accountId, DateOnly? earliestEmailReceivedDate) =>
        new() { Accounts = [CreateAccount(accountId, earliestEmailReceivedDate)] };

    private static MailSynchronizationAccountOptions CreateAccount(string accountId, DateOnly? earliestEmailReceivedDate) => new()
    {
        AccountId = accountId,
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        EarliestEmailReceivedDate = earliestEmailReceivedDate,
    };
}
