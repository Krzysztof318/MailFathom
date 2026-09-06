// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What one answering run has consumed so far, in the units the ceilings that stop it are stated in.</summary>
/// <param name="ProviderCalls">Calls the run has made of the calls one run may make.</param>
/// <param name="Tokens">Tokens the run has been reported to consume, of the tokens one run may spend.</param>
/// <param name="RetrievedCharacters">Characters of retrieved mail the run has sent, of the characters one run may draw out of the mailbox.</param>
/// <param name="MessagesRetrieved">How many distinct messages those characters came from.</param>
/// <remarks>
/// <para>
/// Three counts and the message count beside the third, which is the figure
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// settles on: each of the three is the same quantity that will stop the run rather than a second measure invented for
/// display, and a character total is the bound while a message count is what a person has an intuition about.
/// </para>
/// <para>
/// <strong>Nothing here is money and nothing here converts to it.</strong> MailFathom holds no price list, a price is a
/// contract between an operator and a provider, and a figure denominated in currency would be a claim about somebody's
/// bill that this product cannot stand behind.
/// </para>
/// <para>
/// <strong>The token count is a floor rather than a bill.</strong> Tokens are counted from what a provider reported, so
/// a call abandoned in flight — by a cancellation, by a timeout, by a dropped connection — advances it by nothing while
/// the provider may still bill it. What is published is what this deployment observed.
/// </para>
/// <para>
/// It is four numbers and carries nothing about the mail, which is what lets it travel to a client while a question is
/// still being answered and lets it be recorded beside a run that failed.
/// </para>
/// </remarks>
public sealed record MailAnsweringRunSpend(
    int ProviderCalls,
    long Tokens,
    int RetrievedCharacters,
    int MessagesRetrieved)
{
    /// <summary>Gets what a run that has spent nothing reports.</summary>
    public static MailAnsweringRunSpend Nothing { get; } = new(0, 0, 0, 0);
}
