// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers what each fact resolves to, what it costs, and what an absent value answers.</summary>
public sealed class MailRuleFactsTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 3, 31, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Every declared fact resolves rather than raising, which is what makes the surface usable as declared.</summary>
    /// <remarks>
    /// Resolved one at a time rather than together, because one instance serves one evaluation and an evaluation asks
    /// for its facts in sequence. Asking concurrently here would test something the contract does not offer.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_EveryDeclaredFact_HasAResolution()
    {
        // Arrange
        var facts = CreateFacts();

        // Act
        foreach (var fact in MailRuleFact.All)
        {
            await facts.ResolveAsync(fact, TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.Equal(MailRuleFact.All, facts.ResolvedFacts);
    }

    [Theory]
    [InlineData("account", "work")]
    [InlineData("folder", "inbox")]
    [InlineData("subject", "March invoice 2026")]
    [InlineData("senderAddress", "billing@supplier.test")]
    [InlineData("senderDomain", "supplier.test")]
    public async Task ResolveAsync_TextFact_AnswersWithTheStoredValue(string name, string expected)
    {
        // Arrange
        Assert.True(MailRuleFact.TryParseName(name, out var fact));

        // Act
        var value = await CreateFacts().ResolveAsync(fact, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, value);
    }

    /// <summary>A domain is derived from its address rather than stored, so the two can never disagree.</summary>
    [Fact]
    public async Task ResolveAsync_RecipientDomains_AreDerivedFromTheAddressesWithoutRepeats()
    {
        // Act
        var value = await CreateFacts().ResolveAsync(
            MailRuleFact.RecipientDomains,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["example.test", "supplier.test"], Assert.IsAssignableFrom<IReadOnlyList<string>>(value));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("@example.test")]
    [InlineData("owner@")]
    [InlineData("")]
    [InlineData(null)]
    public void SenderDomain_AddressWithoutOne_IsAbsentRatherThanEmpty(string? address)
    {
        // Arrange
        var email = new MailRuleEmailFacts { Account = "work", Folder = "inbox", SenderAddress = address };

        // Assert
        Assert.Null(email.SenderDomain);
    }

    /// <summary>The last at sign separates the domain, because a quoted local part may legitimately hold one.</summary>
    [Fact]
    public void SenderDomain_AddressWhoseLocalPartHoldsAnAtSign_TakesTheLastOne()
    {
        // Arrange
        var email = new MailRuleEmailFacts
        {
            Account = "work",
            Folder = "inbox",
            SenderAddress = "\"odd@local\"@supplier.test",
        };

        // Assert
        Assert.Equal("supplier.test", email.SenderDomain);
    }

    [Fact]
    public async Task ResolveAsync_NumericFacts_AnswerAsOneNumericTypeWhateverTheyAreStoredIn()
    {
        // Arrange
        var facts = CreateFacts();
        var numericFacts = MailRuleFact.All
            .Where(fact => fact.ValueType == MailRuleFactType.Number)
            .ToArray();

        // Act
        var values = new List<object?>(numericFacts.Length);

        foreach (var fact in numericFacts)
        {
            values.Add(await facts.ResolveAsync(fact, TestContext.Current.CancellationToken));
        }

        // Assert
        Assert.All(values, value => Assert.IsType<double>(value));
    }

    [Fact]
    public async Task ResolveAsync_AgeInDays_IsMeasuredFromTheInstantThePassStarted()
    {
        // Act
        var value = await CreateFacts().ResolveAsync(MailRuleFact.AgeInDays, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(30d, Assert.IsType<double>(value), 6);
    }

    [Fact]
    public async Task ResolveAsync_Timestamp_AnswersInCoordinatedUniversalTime()
    {
        // Arrange
        var facts = new MailRuleFacts(
            new MailRuleEmailFacts
            {
                Account = "work",
                Folder = "inbox",
                ReceivedAt = new DateTimeOffset(2026, 3, 1, 11, 30, 0, TimeSpan.FromHours(2)),
            },
            new RecordingMailRuleBodyTextReader(),
            EvaluatedAt);

        // Act
        var value = await facts.ResolveAsync(MailRuleFact.ReceivedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc), Assert.IsType<DateTime>(value));
    }

    [Fact]
    public async Task ResolveAsync_FactTheEmailCarriesNothingFor_AnswersWithAbsence()
    {
        // Arrange
        var facts = new MailRuleFacts(
            new MailRuleEmailFacts { Account = "work", Folder = "inbox" },
            new RecordingMailRuleBodyTextReader(),
            EvaluatedAt);

        // Act
        var subject = await facts.ResolveAsync(MailRuleFact.Subject, TestContext.Current.CancellationToken);
        var age = await facts.ResolveAsync(MailRuleFact.AgeInDays, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(subject);
        Assert.Null(age);
    }

    [Fact]
    public async Task ResolveAsync_BodyTextAskedForTwice_ReadsStoredContentOnce()
    {
        // Arrange
        var bodyTextReader = new RecordingMailRuleBodyTextReader("Amount due on receipt.");
        var facts = CreateFacts(bodyTextReader);

        // Act
        await facts.ResolveAsync(MailRuleFact.BodyText, TestContext.Current.CancellationToken);
        var second = await facts.ResolveAsync(MailRuleFact.BodyText, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Amount due on receipt.", second);
        Assert.Equal(1, bodyTextReader.ReadCount);
    }

    [Fact]
    public async Task ResolveAsync_FactsNobodyAskedFor_AreNeverResolved()
    {
        // Arrange
        var bodyTextReader = new RecordingMailRuleBodyTextReader("Amount due on receipt.");
        var facts = CreateFacts(bodyTextReader);

        // Act
        await facts.ResolveAsync(MailRuleFact.Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailRuleFact.Account], facts.ResolvedFacts);
        Assert.Equal(0, bodyTextReader.ReadCount);
    }

    [Fact]
    public async Task ResolveAsync_UnspecifiedFact_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateFacts().ResolveAsync(default, TestContext.Current.CancellationToken));
    }

    private static MailRuleFacts CreateFacts(RecordingMailRuleBodyTextReader? bodyTextReader = null) =>
        new(
            new MailRuleEmailFacts
            {
                Account = "work",
                Folder = "inbox",
                Subject = "March invoice 2026",
                SenderAddress = "billing@supplier.test",
                RecipientAddresses = ["owner@example.test", "accounts@example.test", "billing@supplier.test"],
                ReceivedAt = ReceivedAt,
                SentAt = ReceivedAt.AddMinutes(-5),
                SizeInBytes = 250_000,
                AttachmentCount = 2,
                AttachmentTotalBytes = 200_000,
                HasExtractedContent = true,
            },
            bodyTextReader ?? new RecordingMailRuleBodyTextReader("Amount due on receipt."),
            EvaluatedAt);
}
