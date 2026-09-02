// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Every ceiling one question about the mailbox is subject to, from what a lookup hands over to what a period may spend.</summary>
/// <param name="Retrieval">What one lookup may draw out of the mailbox.</param>
/// <param name="Run">What one run may send, call, and consume.</param>
/// <param name="Period">What the runs of one period may add up to.</param>
/// <param name="Answer">How much of a run's outcome one response publishes.</param>
/// <remarks>
/// <para>
/// The four are declared together because an operator reads them together — they are one decision about how much
/// answering costs and how much mail leaves — and they are separate values because they are enforced in four places:
/// where the passages are built, where a provider call is about to be made, where a question is admitted, and where a
/// response is written. A composition root maps the declaration onto this and registers the parts; nothing downstream
/// resolves the whole.
/// </para>
/// <para>
/// It holds no credential, no endpoint, and nothing about which provider answers. What a question may spend is a
/// property of this deployment rather than of the model it happens to be pointed at, so an instance that answers no
/// questions still has these ceilings and simply never reaches one.
/// </para>
/// </remarks>
public sealed record MailAnsweringBudget(
    EmailKnowledgeBounds Retrieval,
    MailAnsweringRunBounds Run,
    MailAnsweringPeriodBounds Period,
    MailAnswerBounds Answer)
{
    /// <summary>Gets the budget a deployment that states none receives.</summary>
    public static MailAnsweringBudget Default { get; } = new(
        EmailKnowledgeBounds.Default,
        MailAnsweringRunBounds.Default,
        MailAnsweringPeriodBounds.Default,
        MailAnswerBounds.Default);
}
