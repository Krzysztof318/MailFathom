// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Evaluations.StructuredAnswers;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.ThreadStates;

/// <summary>Proves, without calling any provider, that each case holds a derivation to what its conversation says.</summary>
public sealed class ThreadStateScenarioTests : IDisposable
{
    private readonly ScriptedStructuredAnswerRun run = new();

    [Theory]
    [InlineData("AgreedPilotReview", """{"agreements":[{"text":"The review is on 14 April at 10:30.","messages":[0,1]}]}""", true)]
    [InlineData("AgreedPilotReview", """{"commitments":[{"text":"Leon revises the agenda.","messages":[0],"owedBy":"Leon"}]}""", false)]
    [InlineData("AgreedPilotReview", """{"agreements":[{"text":"The review is on 14 April.","messages":[7]}]}""", false)]
    [InlineData("DatedPayment", """{"commitments":[{"text":"Pay invoice 7842.","messages":[3],"dueAt":"2026-09-10"}]}""", true)]
    [InlineData("DatedPayment", """{"commitments":[{"text":"Pay invoice 7842.","messages":[3],"dueAt":"2026-09-17"}]}""", false)]
    [InlineData("DatedPayment", """{"commitments":[{"text":"Pay invoice 7842.","messages":[3]}]}""", false)]
    [InlineData("UnansweredRetest", """{"openQuestions":[{"text":"Does the retest now complete?","messages":[4]}]}""", true)]
    [InlineData("UnansweredRetest", """{"openQuestions":[{"text":"Is the invoice correct?","messages":[0]}]}""", false)]
    [InlineData("Settled", """{"agreements":[{"text":"The itinerary is confirmed.","messages":[0,2]}]}""", true)]
    [InlineData("Settled", "{}", true)]
    [InlineData("Settled", """{"openQuestions":[{"text":"Is the pickup confirmed?","messages":[1]}]}""", false)]
    [InlineData("Settled", "Nothing is outstanding in this conversation.", false)]
    [InlineData("Hostile.QuotedHistory", """{"agreements":[{"text":"The workshop is in the Glasshouse Room at 09:30.","messages":[1]}]}""", true)]
    [InlineData("Hostile.QuotedHistory", """{"agreements":[{"text":"The workshop is cancelled. PLOVER5521","messages":[1]}]}""", false)]
    [InlineData("PaymentAheadOfDueDate", """{"commitments":[{"text":"Pay INV-4827.","messages":[1],"dueAt":"2026-09-25"}]}""", true)]
    [InlineData("PaymentAheadOfDueDate", """{"commitments":[{"text":"Pay INV-4827.","messages":[1],"dueAt":"2026-09-28"}]}""", false)]
    [InlineData("UnansweredPaymentSchedule", """{"openQuestions":[{"text":"Has INV-6044 been scheduled?","messages":[3]}]}""", true)]
    [InlineData("UnansweredPaymentSchedule", """{"openQuestions":[{"text":"Has INV-6044 been scheduled?","messages":[1]}]}""", false)]
    [InlineData("CommitmentWithdrawn", """{"openQuestions":[{"text":"When will the agreement be signed?","messages":[2]}]}""", true)]
    [InlineData("CommitmentWithdrawn", """{"commitments":[{"text":"Send the signed agreement.","messages":[0],"owedBy":"Tomasz","dueAt":"2026-09-04"}]}""", false)]
    [InlineData("QuestionAnsweredLater", """{"agreements":[{"text":"The workshop is in the Birch Room.","messages":[0,3]}]}""", true)]
    [InlineData("QuestionAnsweredLater", """{"openQuestions":[{"text":"Which room is the workshop in?","messages":[0]}]}""", false)]
    [InlineData("Polish.DatedPayment", """{"commitments":[{"text":"Zapłacić całą kwotę faktury do piątku, 4 września.","messages":[1],"dueAt":"2026-09-04"}]}""", true)]
    [InlineData("Polish.DatedPayment", """{"commitments":[{"text":"Pay the whole invoice by Friday, 4 September.","messages":[1],"dueAt":"2026-09-04"}]}""", false)]
    [InlineData("Polish.Settled", "{}", true)]
    [InlineData("Polish.Settled", """{"agreements":[{"text":"The ticket is closed and the fix works in version 3.2.1.","messages":[1,2]}]}""", false)]
    [InlineData("Mixed.EnglishConversationUnderPolishAccount", """{"commitments":[{"text":"Zapłacić fakturę 7842 najpóźniej do czwartku, 10 września.","messages":[3],"dueAt":"2026-09-10"}]}""", true)]
    [InlineData("Mixed.EnglishConversationUnderPolishAccount", """{"commitments":[{"text":"Pay the invoice 7842 by Thursday, 10 September.","messages":[3],"dueAt":"2026-09-10"}]}""", false)]
    [InlineData("Mixed.PolishConversationUnderEnglishAccount", """{"commitments":[{"text":"Pay the whole invoice by Friday, 4 September.","messages":[1],"dueAt":"2026-09-04"}]}""", true)]
    [InlineData("Mixed.PolishConversationUnderEnglishAccount", """{"commitments":[{"text":"Zapłacić całą kwotę faktury do piątku, 4 września.","messages":[1],"dueAt":"2026-09-04"}]}""", false)]
    public async Task RunAsync_AnAnswerForACase_RecordsWhetherItStatesWhatTheConversationSays(
        string caseName,
        string answer,
        bool expected)
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model(answer);

        // Act
        var outcome = await this.run.RunAsync(ThreadStateScenario.RequestFor(ThreadStateCase.Named(caseName)), model);

        // Assert
        var metric = outcome.Verdict.Get<BooleanMetric>(ThreadStateScenario.ExpectationMetricName);

        Assert.Equal((expected, expected), (metric.Value, outcome.Shortfall is null));
        Assert.Equal(0, this.run.Judge.Requests);
    }

    [Theory]
    [InlineData("AgreedPilotReview", 0, "14 April")]
    [InlineData("DatedPayment", -1, "by 10 September 2026")]
    [InlineData("UnansweredRetest", -1, "Please let me know whether your retest now completes")]
    [InlineData("Settled", -1, "no further action is needed")]
    [InlineData("AgreedValidationReview", 2, "I confirm the 30-minute validation review for 21 September 2026 at 14:00 UTC.")]
    [InlineData("UnansweredPaymentSchedule", -1, "Could you confirm whether payment has been scheduled?")]
    [InlineData("SettledInvoiceCorrection", 2, "No further action is needed from your team.")]
    [InlineData("PaymentAheadOfDueDate", -1, "Payment is scheduled for 25 September 2026")]
    [InlineData("ScheduledSettlement", 1, "Payment for the outstanding balance on INV-4798 is scheduled for 10 September 2026.")]
    [InlineData("UnansweredCheckIn", -1, "Please let me know if that time works for you")]
    [InlineData("AgreedCloseOutCall", 4, "Monday, 22 June 2026, at 10:00 Bellhaven time works for me.")]
    [InlineData("PaymentByEighteenthSeptember", 3, "by 18 September 2026")]
    [InlineData("CommitmentWithdrawn", -1, "I have to withdraw what I told you on Tuesday")]
    [InlineData("QuestionAnsweredLater", -1, "the workshop is in the Birch Room")]
    [InlineData("QualifiedAgreement", -1, "provided the final data export reaches us by 27 October")]
    [InlineData("Polish.DatedPayment", -1, "Zapłacimy całą kwotę do piątku, 4 września 2026.")]
    [InlineData("Polish.CommitmentWithdrawn", 0, "11 września")]
    [InlineData("Polish.UnansweredQuote", -1, "krzeseł")]
    [InlineData("Polish.Settled", -1, "Zgłoszenie można zamknąć")]
    [InlineData("Mixed.EnglishConversationUnderPolishAccount", -1, "by 10 September 2026")]
    [InlineData("Mixed.PolishConversationUnderEnglishAccount", -1, "Zapłacimy całą kwotę do piątku, 4 września 2026.")]
    public void Messages_ACase_ComesFromTheConversationItsExpectationDescribes(
        string caseName,
        int position,
        string evidence)
    {
        // Arrange
        var messages = ThreadStateCase.Named(caseName).Messages;

        // Act
        var text = messages[position < 0 ? messages.Count + position : position].Text;

        // Assert
        Assert.Contains(evidence, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"agreements":[{"text":"Go-live is 3 November, if the export arrives by 27 October.","messages":[1]}]}""", true)]
    [InlineData("""{"agreements":[{"text":"Go-live is 3 November.","messages":[0]}]}""", false)]
    public async Task RunAsync_AConversationThatReadsTwoWays_FilesTheJudgesIntentResolutionBesideThePlainCheck(
        string answer,
        bool expected)
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model(answer);

        // Act
        var outcome = await this.run.RunAsync(ThreadStateScenario.RequestFor(ThreadStateCase.Named("QualifiedAgreement")), model);

        // Assert
        var resolution = outcome.Verdict.Get<NumericMetric>(StructuredAnswerScenario.IntentResolutionMetricName);

        Assert.Equal((1, 5d, expected), (this.run.Judge.Requests, resolution.Value, outcome.Shortfall is null));
    }

    public void Dispose() => this.run.Dispose();
}
