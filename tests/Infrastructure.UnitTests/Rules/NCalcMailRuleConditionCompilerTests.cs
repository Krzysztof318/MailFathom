// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Infrastructure.Rules;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Rules;

/// <summary>Covers what reading a condition accepts, what it refuses, and what every refusal has to say.</summary>
public sealed class NCalcMailRuleConditionCompilerTests
{
    private const string RuleName = "file-invoices";

    private readonly NCalcMailRuleConditionCompiler compiler = new();

    /// <summary>The shapes the documented surface promises, each of which has to survive the walk that checks it.</summary>
    [Theory]
    [InlineData("senderDomain == 'example.test'")]
    [InlineData("not isSeen")]
    [InlineData("isEncrypted or carriesUnverifiedSignature")]
    [InlineData("folder in ('inbox', 'archive')")]
    [InlineData("folder not in ('spam', 'trash')")]
    [InlineData("in(folder, 'inbox', 'archive')")]
    [InlineData("!isSeen && isDraft || isFlagged")]
    [InlineData("sizeInBytes > 1000000")]
    [InlineData("attachmentCount >= 1 and ageInDays < 30")]
    [InlineData("receivedAt >= #2026/01/01#")]
    [InlineData("contains(subject, 'invoice')")]
    [InlineData("contains(recipientDomains, 'example.test')")]
    [InlineData("startsWith(subject, 're:') or endsWith(senderAddress, '.test')")]
    [InlineData("isNull(subject)")]
    [InlineData("isNullOrEmpty(bodyText)")]
    [InlineData("if(isSeen, sizeInBytes, 0) > 100")]
    [InlineData("isSeen ? attachmentCount > 0 : false")]
    [InlineData("(senderDomain == 'example.test' and attachmentCount > 0) or ageInDays > 90")]
    [InlineData("sizeInBytes / 1024 > 500")]
    public void Compile_ConditionOverTheDeclaredSurface_IsCompiled(string conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.True(compilation.IsCompiled, string.Join(" ", compilation.Errors));
    }

    [Fact]
    public void Compile_Condition_ReportsOnlyTheFactsItNames()
    {
        // Act
        var compilation = this.compiler.Compile(
            RuleName,
            "senderDomain == 'example.test' and contains(subject, 'invoice') and senderDomain != 'other.test'",
            MailRuleConditionBounds.Default);

        // Assert
        Assert.True(compilation.IsCompiled, string.Join(" ", compilation.Errors));
        Assert.Equal(
            [MailRuleFact.SenderDomain, MailRuleFact.Subject],
            compilation.Condition.ReferencedFacts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compile_BlankCondition_IsRefused(string? conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Contains(compilation.Errors, error => error.Contains("is missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ConditionThatDoesNotParse_IsRefusedWithoutQuotingWhatWasWritten()
    {
        // Act
        var compilation = this.compiler.Compile(
            RuleName,
            "senderDomain == 'confidential@example.test",
            MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.All(
            compilation.Errors,
            error => Assert.DoesNotContain("confidential@example.test", error, StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ConditionNamingSomethingThatIsNotAFact_IsRefused()
    {
        // Act
        var compilation = this.compiler.Compile(
            RuleName,
            "senderMailbox == 'example.test'",
            MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Contains(compilation.Errors, error => error.Contains("senderMailbox", StringComparison.Ordinal));
        Assert.All(compilation.Errors, error => Assert.Contains(RuleName, error, StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ConditionCallingSomethingThatIsNotAFunction_IsRefused()
    {
        // Act
        var compilation = this.compiler.Compile(
            RuleName,
            "matches(subject, 'invoice')",
            MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Contains(compilation.Errors, error => error.Contains("matches", StringComparison.Ordinal));
    }

    /// <summary>The whole mathematical library is outside the surface, and naming any of it is refused rather than run.</summary>
    [Theory]
    [InlineData("Sqrt(sizeInBytes) > 10")]
    [InlineData("Max(sizeInBytes, attachmentCount) > 10")]
    [InlineData("ifs(isSeen, 1, 2) > 1")]
    public void Compile_ConditionCallingAFunctionOutsideTheSurface_IsRefused(string conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
    }

    /// <summary>Every operator the language offers and this surface does not admit, including the two that bound cost.</summary>
    [Theory]
    [InlineData("(sizeInBytes & 1) == 1")]
    [InlineData("(sizeInBytes | 1) == 1")]
    [InlineData("(sizeInBytes << 2) > 1")]
    [InlineData("sizeInBytes ** 2 > 1")]
    [InlineData("subject like 'invoice%'")]
    [InlineData("(senderDomain ?? 'none') == 'none'")]
    public void Compile_ConditionUsingAnOperatorOutsideTheSurface_IsRefused(string conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
    }

    [Theory]
    [InlineData("subject == 1")]
    [InlineData("sizeInBytes > 'large'")]
    [InlineData("receivedAt == 'yesterday'")]
    [InlineData("isSeen == 1")]
    [InlineData("recipientDomains == 'example.test'")]
    [InlineData("subject > 'a'")]
    [InlineData("not sizeInBytes")]
    [InlineData("isSeen and sizeInBytes")]
    [InlineData("folder in ('inbox', 3)")]
    [InlineData("in(folder, 3)")]
    [InlineData("contains(sizeInBytes, 'x')")]
    [InlineData("contains(subject, 3)")]
    [InlineData("startsWith(subject, 3)")]
    [InlineData("isNullOrEmpty(sizeInBytes)")]
    [InlineData("if(sizeInBytes, 1, 2) > 1")]
    [InlineData("if(isSeen, 1, 'two') == 1")]
    public void Compile_ComparisonOrCallThatCouldNeverHold_IsRefused(string conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.All(compilation.Errors, error => Assert.Contains(RuleName, error, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("sizeInBytes + 1")]
    [InlineData("'invoice'")]
    public void Compile_ConditionThatDoesNotProduceABoolean_IsRefused(string conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Contains(compilation.Errors, error => error.Contains("boolean", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ConditionLongerThanTheLimit_IsRefusedBeforeItIsParsed()
    {
        // Arrange
        var bounds = MailRuleConditionBounds.Create(maxLength: 20, maxNestingDepth: 16, TimeSpan.FromSeconds(1));

        // Act
        var compilation = this.compiler.Compile(RuleName, "senderDomain == 'example.test'", bounds);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Contains(compilation.Errors, error => error.Contains("at most 20", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ConditionNestedDeeperThanTheLimit_IsRefusedOnce()
    {
        // Arrange
        var bounds = MailRuleConditionBounds.Create(maxLength: 1_000, maxNestingDepth: 3, TimeSpan.FromSeconds(1));

        // Act
        var compilation = this.compiler.Compile(
            RuleName,
            "isSeen and (isDraft and (isFlagged and (isAnswered and isEncrypted)))",
            bounds);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Single(compilation.Errors);
        Assert.Contains(compilation.Errors, error => error.Contains("3 levels", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_ConditionWithSeveralDefects_ReportsAllOfThem()
    {
        // Act
        var compilation = this.compiler.Compile(
            RuleName,
            "senderMailbox == 'a' and recipientMailbox == 'b'",
            MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Equal(2, compilation.Errors.Count);
    }

    /// <summary>
    /// A single parenthesized value is not a list to this parser, and the language's own membership operator would
    /// have searched a string for a substring instead of comparing it. Both shapes are refused with the message that
    /// says what to write.
    /// </summary>
    [Theory]
    [InlineData("folder in 'inbox'")]
    [InlineData("folder in ('inbox')")]
    public void Compile_MembershipWithoutAListAfterIt_IsRefused(string conditionText)
    {
        // Act
        var compilation = this.compiler.Compile(RuleName, conditionText, MailRuleConditionBounds.Default);

        // Assert
        Assert.False(compilation.IsCompiled);
        Assert.Contains(
            compilation.Errors,
            error => error.Contains("parenthesized list of values", StringComparison.Ordinal));
    }
}
