// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Corpus;

/// <summary>The committed Polish corpus, <c>office-pl.zip</c>, read beside the English one and never into it.</summary>
/// <remarks>
/// <para>
/// Written by hand for this suite in the archive shape the English corpora use: an invoice chased and paid, a support
/// ticket fixed and closed, a trip planned, an office move whose day a later message corrects, a quotation chased, an
/// undertaking withdrawn, and a newsletter — the kinds of mail the English cases already ask about, so a Polish case
/// measures the language rather than a new kind of task. Every address sits under a reserved domain no English corpus
/// uses, so a Polish conversation never joins an English correspondent's.
/// </para>
/// <para>
/// It stays out of <see cref="CorpusMessage.All" />, because the English cases search that mailbox and would read a new
/// message as a changed answer. A case that asks about Polish mail searches <see cref="MixedMailbox" /> instead, which is
/// the mailbox a person reading both languages actually has. Its positions start past every other message the suite
/// writes, so an identifier derived from one never names another.
/// </para>
/// </remarks>
internal static class PolishCorpus
{
    /// <summary>Where the corpus's positions begin, past the hostile mail's.</summary>
    private const int FirstPosition = 20_000;

    private static readonly Lazy<IReadOnlyList<IReadOnlyList<CorpusMessage>>> Delivered = new(static () =>
        CorpusMessage.ReadArchives(["office-pl.zip"], FirstPosition));

    /// <summary>Gets every conversation of the corpus, in delivery order, each in the order its messages were written.</summary>
    public static IReadOnlyList<IReadOnlyList<CorpusMessage>> Exchanges => Delivered.Value;

    /// <summary>Gets every message of the corpus, in delivery order.</summary>
    public static IReadOnlyList<CorpusMessage> All => [.. Exchanges.SelectMany(static exchange => exchange)];

    /// <summary>Gets the mailbox a question in either language is asked over: the English mailbox, then the Polish corpus.</summary>
    public static IReadOnlyList<CorpusMessage> MixedMailbox => [.. HostileMail.Mailbox, .. All];

    /// <summary>Reads one message of the corpus.</summary>
    /// <param name="position">Where the message falls in the corpus's own delivery order, from zero.</param>
    /// <returns>The message.</returns>
    public static CorpusMessage At(int position) => All[position];

    /// <summary>Reads the conversation one message closes: its exchange up to and including it, oldest first.</summary>
    /// <param name="position">Where the closing message falls in the corpus's own delivery order.</param>
    /// <returns>The conversation as it stood when that message arrived.</returns>
    public static IReadOnlyList<CorpusMessage> ConversationUpTo(int position)
    {
        var closing = All[position];
        var exchange = Exchanges.Single(conversation => conversation.Contains(closing));

        return [.. exchange.TakeWhile(message => message != closing), closing];
    }
}
