// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers the refusal a deployment gives when answering would cost more than it agreed to spend.</summary>
/// <remarks>
/// The two scopes carry one code and differ in the message, exactly as the capability failure beside them does. What is
/// asserted here is that a caller can still tell them apart, because only one of the two becomes answerable by waiting.
/// </remarks>
public sealed class MailAnsweringBudgetExhaustedExceptionTests
{
    [Fact]
    public void PeriodSpent_TheRefusal_CarriesTheCodeAndSaysTheAllowanceReturns()
    {
        // Act
        var failure = MailAnsweringBudgetExhaustedException.PeriodSpent();

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAnsweringBudgetExhausted, failure.ErrorCode);
        Assert.Equal(MailAnsweringBudgetScope.Period, failure.Scope);
        Assert.Contains("period", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunSpent_TheRefusal_CarriesTheCodeAndPointsAtTheQuestion()
    {
        // Act
        var failure = MailAnsweringBudgetExhaustedException.RunSpent();

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAnsweringBudgetExhausted, failure.ErrorCode);
        Assert.Equal(MailAnsweringBudgetScope.Run, failure.Scope);
        Assert.Contains("narrower question", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A ceiling is what the operator agreed to spend, and how much of a mailbox that buys is not the caller's to read.</summary>
    [Fact]
    public void Messages_NeitherRefusal_NamesACeilingACallerCouldNotInfluence()
    {
        // Act
        string[] messages =
        [
            MailAnsweringBudgetExhaustedException.PeriodSpent().Message,
            MailAnsweringBudgetExhaustedException.RunSpent().Message,
        ];

        // Assert
        Assert.All(messages, message => Assert.DoesNotContain(message.ToCharArray(), char.IsAsciiDigit));
    }
}
