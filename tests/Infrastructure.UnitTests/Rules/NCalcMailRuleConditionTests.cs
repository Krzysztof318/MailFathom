// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Infrastructure.Rules;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Rules;

/// <summary>Covers what a compiled condition answers, and what it costs to answer it.</summary>
public sealed class NCalcMailRuleConditionTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 3, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 3, 31, 9, 30, 0, TimeSpan.Zero);

    private readonly NCalcMailRuleConditionCompiler compiler = new();

    /// <summary>One email, every declared metadata fact, and the conditions an owner would write about it.</summary>
    [Theory]
    [InlineData("account == 'work'", true)]
    [InlineData("account == 'personal'", false)]
    [InlineData("folder in ('inbox', 'archive')", true)]
    [InlineData("folder in ('sent', 'archive')", false)]
    [InlineData("senderDomain == 'supplier.test'", true)]
    [InlineData("senderAddress == 'billing@supplier.test'", true)]
    [InlineData("contains(recipientDomains, 'example.test')", true)]
    [InlineData("contains(recipientDomains, 'nobody.test')", false)]
    [InlineData("contains(recipientAddresses, 'owner@example.test')", true)]
    [InlineData("contains(subject, 'invoice')", true)]
    [InlineData("startsWith(subject, 'March')", true)]
    [InlineData("endsWith(subject, '2026')", true)]
    [InlineData("sizeInBytes > 100000", true)]
    [InlineData("sizeInBytes / 1024 > 1000", false)]
    [InlineData("attachmentCount == 2", true)]
    [InlineData("attachmentTotalBytes > 100000", true)]
    [InlineData("ageInDays > 29 and ageInDays < 31", true)]
    [InlineData("receivedAt >= #2026/01/01#", true)]
    [InlineData("receivedAt < #2026/01/01#", false)]
    [InlineData("sentAt < receivedAt", true)]
    [InlineData("isSeen", false)]
    [InlineData("not isSeen", true)]
    [InlineData("isFlagged and not isDraft", true)]
    [InlineData("isEncrypted or carriesUnverifiedSignature", true)]
    [InlineData("hasExtractedContent", true)]
    [InlineData("contains(bodyText, 'due on receipt')", true)]
    [InlineData("if(isSeen, 0, attachmentCount) > 1", true)]
    [InlineData("isFlagged ? attachmentCount > 1 : false", true)]
    public async Task EvaluateAsync_ConditionOverTheDeclaredFacts_AnswersFromTheEmail(
        string conditionText,
        bool expectedMatch)
    {
        // Arrange
        var condition = this.Compile(conditionText);
        var facts = CreateFacts();

        // Act
        var matched = await condition.EvaluateAsync(facts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedMatch, matched);
    }

    /// <summary>Text comparison ignores case throughout, so an owner never has to guess how a subject was capitalized.</summary>
    [Theory]
    [InlineData("senderDomain == 'SUPPLIER.TEST'")]
    [InlineData("contains(subject, 'INVOICE')")]
    [InlineData("startsWith(subject, 'march')")]
    [InlineData("contains(recipientDomains, 'EXAMPLE.TEST')")]
    [InlineData("folder in ('INBOX', 'ARCHIVE')")]
    public async Task EvaluateAsync_TextDifferingOnlyByCase_StillMatches(string conditionText)
    {
        // Arrange
        var condition = this.Compile(conditionText);

        // Act
        var matched = await condition.EvaluateAsync(CreateFacts(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(matched);
    }

    /// <summary>An absent fact answers rather than failing, which is what keeps an unusual email from failing a rule.</summary>
    [Theory]
    [InlineData("subject == 'anything'", false)]
    [InlineData("subject != 'anything'", true)]
    [InlineData("isNull(subject)", true)]
    [InlineData("isNullOrEmpty(subject)", true)]
    [InlineData("contains(subject, 'anything')", false)]
    [InlineData("startsWith(subject, 'anything')", false)]
    [InlineData("isNull(receivedAt)", true)]
    [InlineData("isNull(ageInDays)", true)]
    public async Task EvaluateAsync_FactTheEmailCarriesNothingFor_AnswersWithoutFailing(
        string conditionText,
        bool expectedMatch)
    {
        // Arrange
        var condition = this.Compile(conditionText);
        var facts = new MailRuleFacts(
            new MailRuleEmailFacts { Account = "work", Folder = "inbox" },
            new RecordingMailRuleBodyTextReader(),
            EvaluatedAt);

        // Act
        var matched = await condition.EvaluateAsync(facts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedMatch, matched);
    }

    [Fact]
    public async Task EvaluateAsync_ConditionNamingNoStoredContent_NeverReadsIt()
    {
        // Arrange
        var condition = this.Compile("senderDomain == 'supplier.test'");
        var bodyTextReader = new RecordingMailRuleBodyTextReader("due on receipt");
        var facts = CreateFacts(bodyTextReader);

        // Act
        await condition.EvaluateAsync(facts, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, bodyTextReader.ReadCount);
        Assert.Equal([MailRuleFact.SenderDomain], facts.ResolvedFacts);
    }

    [Fact]
    public async Task EvaluateAsync_ConditionNamingStoredContentTwice_ReadsItOnce()
    {
        // Arrange
        var condition = this.Compile("contains(bodyText, 'due') and contains(bodyText, 'receipt')");
        var bodyTextReader = new RecordingMailRuleBodyTextReader("due on receipt");
        var facts = CreateFacts(bodyTextReader);

        // Act
        var matched = await condition.EvaluateAsync(facts, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(matched);
        Assert.Equal(1, bodyTextReader.ReadCount);
    }

    /// <summary>The boolean operators short-circuit, so the half of a condition that decides nothing costs nothing.</summary>
    [Fact]
    public async Task EvaluateAsync_ConditionWhoseFirstOperandDecidesIt_LeavesTheRestUnresolved()
    {
        // Arrange
        var condition = this.Compile("account == 'personal' and contains(bodyText, 'due')");
        var bodyTextReader = new RecordingMailRuleBodyTextReader("due on receipt");
        var facts = CreateFacts(bodyTextReader);

        // Act
        var matched = await condition.EvaluateAsync(facts, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(matched);
        Assert.Equal(0, bodyTextReader.ReadCount);
    }

    [Fact]
    public async Task EvaluateAsync_CancelledEvaluation_StopsRatherThanAnswering()
    {
        // Arrange
        var condition = this.Compile("contains(bodyText, 'due')");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => condition.EvaluateAsync(CreateFacts(), cancellation.Token));
    }

    /// <summary>Arithmetic that leaves its range raises, so a pass records the rule as failed rather than as an answer.</summary>
    /// <remarks>
    /// Wrapping is the failure worth catching here, because a wrapped value is a number the comparison above it accepts:
    /// a condition on a size would go on matching, quietly, against a value nothing in the email produced. The evaluator
    /// classifies the raised failure; what this proves is that there is one to classify.
    /// </remarks>
    [Fact]
    public async Task EvaluateAsync_ArithmeticThatLeavesItsRange_RaisesRatherThanWrapping()
    {
        // Arrange
        var condition = this.Compile("9223372036854775807 + 1 > 0");

        // Act, Assert
        await Assert.ThrowsAnyAsync<Exception>(
            () => condition.EvaluateAsync(CreateFacts(), TestContext.Current.CancellationToken));
    }

    private static MailRuleFacts CreateFacts(RecordingMailRuleBodyTextReader? bodyTextReader = null) =>
        new(
            new MailRuleEmailFacts
            {
                Account = "work",
                Folder = "inbox",
                Subject = "March invoice 2026",
                SenderAddress = "billing@supplier.test",
                RecipientAddresses = ["owner@example.test", "accounts@example.test"],
                ReceivedAt = ReceivedAt,
                SentAt = ReceivedAt.AddMinutes(-5),
                SizeInBytes = 250_000,
                AttachmentCount = 2,
                AttachmentTotalBytes = 200_000,
                IsEncrypted = false,
                CarriesUnverifiedSignature = true,
                IsSeen = false,
                IsAnswered = false,
                IsFlagged = true,
                IsDraft = false,
                HasExtractedContent = true,
            },
            bodyTextReader ?? new RecordingMailRuleBodyTextReader("Amount due on receipt."),
            EvaluatedAt);

    private IMailRuleCondition Compile(string conditionText)
    {
        var compilation = this.compiler.Compile("rule", conditionText, MailRuleConditionBounds.Default);

        Assert.True(compilation.IsCompiled, string.Join(" ", compilation.Errors));

        return compilation.Condition;
    }
}
