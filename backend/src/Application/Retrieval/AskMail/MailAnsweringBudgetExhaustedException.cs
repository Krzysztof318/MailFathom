// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>The failure raised when answering a question would cost more than this deployment agreed to spend.</summary>
/// <remarks>
/// <para>
/// It is not a degradation and nothing is wrong with the deployment: the operator declared a ceiling and it has been
/// reached. That is why it is separate from <see cref="MailAnsweringUnavailableException" />, which says the capability
/// cannot serve — a client told the wrong one of the two either waits for a repair that is not coming or retries
/// something that will not become cheaper.
/// </para>
/// <para>
/// Neither message names a ceiling, a count, or a model. A number a caller cannot influence is not something they can
/// act on, and publishing how much a deployment spends on mail is publishing something about the mailbox; the operator
/// reads both from the meter instead.
/// </para>
/// </remarks>
public sealed class MailAnsweringBudgetExhaustedException : MailFathomException
{
    private MailAnsweringBudgetExhaustedException(string operatorSafeMessage, MailAnsweringBudgetScope scope)
        : base(operatorSafeMessage) => this.Scope = scope;

    /// <summary>Gets which ceiling the question reached.</summary>
    public MailAnsweringBudgetScope Scope { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailAnsweringBudgetExhausted;

    /// <summary>Refuses a question the current period has no allowance left for.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>The period turns over on its own, so the message says the same question is worth asking later and nothing about the request caused the refusal.</remarks>
    public static MailAnsweringBudgetExhaustedException PeriodSpent() => new(
        "This deployment has spent what it allows answering to cost for the current period. Nothing about the request caused it, and the allowance returns when the period turns over.",
        MailAnsweringBudgetScope.Period);

    /// <summary>Ends a run that reached what one question may spend.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Raised in place of an answer rather than beside a partial one, because the run was stopped before the model
    /// wrote anything to publish. The message points at the one thing the caller can change, which is how much the
    /// question asks for.
    /// </remarks>
    public static MailAnsweringBudgetExhaustedException RunSpent() => new(
        "Answering this question reached what this deployment allows one question to spend, so the run was stopped before an answer was written. A narrower question costs less.",
        MailAnsweringBudgetScope.Run);
}
