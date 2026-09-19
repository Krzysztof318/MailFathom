// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MimeKit;

namespace MailFathom.Evaluations.Corpus;

/// <summary>The conversations written for this suite by hand, each one a kind of mail the synthetic corpus carries no example of.</summary>
/// <remarks>
/// <para>
/// The synthetic corpus was generated as polite business exchanges that all settle: nobody in it is copied, nothing in it
/// is a newsletter, no reply quotes the message it answers, and nobody withdraws what they promised. A case that needs one
/// of those is given a conversation from here instead. Each is written the way the corpus is — invented names, every
/// address under a reserved domain, nothing anybody received — and read through <see cref="CorpusMessage.Of" />, so its
/// passages and its trimmed text are cut by the same rules a corpus message's are.
/// </para>
/// <para>
/// These conversations stay out of <see cref="CorpusMessage.All" />, because the scenarios that search the corpus are
/// measured against what it holds and would read a new message as a changed answer. Only a case naming one of them, and
/// the correspondence a contact case builds, ever meets them. Their positions start well past the corpus's own, so an
/// identifier derived from one never names a corpus message.
/// </para>
/// </remarks>
internal static class WrittenCorpus
{
    private const string Owner = "owner@example.test";

    /// <summary>Gets a supplier undertaking to send a signed agreement by a named day, and withdrawing that two days later.</summary>
    public static IReadOnlyList<CorpusMessage> CommitmentWithdrawn { get; } = Exchange(
        1000,
        Mail("tomasz.ferreira@brightkeel.test", "Tomasz Ferreira", [Owner], [], At(9, 1, 9, 12), "Signed supplier agreement", """
            Hello Mara,

            The supplier agreement is with our legal team for signature. I will send you the signed copy by Friday, 4 September.

            Best regards,
            Tomasz
            """),
        Mail(Owner, "Mara", ["tomasz.ferreira@brightkeel.test"], [], At(9, 1, 14, 30), "Re: Signed supplier agreement", """
            Hello Tomasz,

            Thank you for letting me know. Friday works for us.

            Best,
            Mara
            """),
        Mail("tomasz.ferreira@brightkeel.test", "Tomasz Ferreira", [Owner], [], At(9, 3, 16, 5), "Re: Signed supplier agreement", """
            Hello Mara,

            I have to withdraw what I told you on Tuesday: I will not be sending the signed agreement on Friday. Our legal team
            has put every supplier contract on hold pending an internal review, and I cannot give you a new date.

            Best regards,
            Tomasz
            """));

    /// <summary>Gets a question about a workshop's room, which two unrelated messages follow and the fourth answers.</summary>
    public static IReadOnlyList<CorpusMessage> QuestionAnsweredLater { get; } = Exchange(
        1010,
        Mail(Owner, "Mara", ["ines.carvalho@lindenrow.test"], [], At(9, 2, 8, 40), "Partner workshop on 22 September", """
            Hi Ines,

            Looking forward to the partner workshop on 22 September. Which room will it be held in?

            Best,
            Mara
            """),
        Mail("ines.carvalho@lindenrow.test", "Ines Carvalho", [Owner], [], At(9, 2, 11, 5), "Re: Partner workshop on 22 September", """
            Hi Mara,

            Glad you can join. The agenda is final: three sessions, starting at 09:30 and closing with lunch at 13:00.

            Best,
            Ines
            """),
        Mail(Owner, "Mara", ["ines.carvalho@lindenrow.test"], [], At(9, 2, 15, 20), "Re: Partner workshop on 22 September", """
            Thanks, Ines. The timing suits us well, and two of us will attend.

            Best,
            Mara
            """),
        Mail("ines.carvalho@lindenrow.test", "Ines Carvalho", [Owner], [], At(9, 4, 9, 50), "Re: Partner workshop on 22 September", """
            Hi Mara,

            Noted, two places are reserved for you. And to answer your earlier question: the workshop is in the Birch Room on
            the second floor.

            Best,
            Ines
            """));

    /// <summary>Gets a proposed go-live date that the other side accepts only on a condition of its own.</summary>
    public static IReadOnlyList<CorpusMessage> QualifiedAgreement { get; } = Exchange(
        1020,
        Mail(Owner, "Mara", ["jonas.weber@quaymark.test"], [], At(9, 7, 10, 0), "Go-live date for the billing migration", """
            Hi Jonas,

            Given the test results, we propose moving the go-live of the billing migration to Tuesday, 3 November 2026. Does
            that work for your team?

            Best,
            Mara
            """),
        Mail("jonas.weber@quaymark.test", "Jonas Weber", [Owner], [], At(9, 8, 13, 25), "Re: Go-live date for the billing migration", """
            Hi Mara,

            We can agree to 3 November, provided the final data export reaches us by 27 October. If it arrives later, go-live
            moves back by a week.

            Best regards,
            Jonas
            """));

    /// <summary>Gets a two-word acknowledgement, which says nothing a reading would be worth writing about.</summary>
    public static CorpusMessage Acknowledgement { get; } = Exchange(
        1030,
        Mail("lena.ortiz@lindenrow.test", "Lena Ortiz", [Owner], [], At(9, 7, 17, 42), "Re: Photos from Friday", """
            Thanks, got them!

            Lena
            """))[0];

    /// <summary>Gets a reply that only acknowledges, quoting beneath it the request it answers.</summary>
    /// <remarks>The request is the owner's own and sits in the quoted history, which a deployment trims before a model is shown the message.</remarks>
    public static CorpusMessage RequestInQuotedHistory { get; } = Exchange(
        1040,
        Mail("karol.lind@quaymark.test", "Karol Lind", [Owner], [], At(9, 8, 8, 3), "Re: Updated rate card", """
            Thanks, received.

            Karol

            On Mon, 7 Sep 2026 at 10:15, Mara <owner@example.test> wrote:
            > Hi Karol,
            >
            > Please send the signed rate card back by Friday, 11 September, so we
            > can issue the purchase order.
            >
            > Best,
            > Mara
            """))[0];

    /// <summary>Gets a monthly newsletter, which informs and asks nothing of the person it reaches.</summary>
    public static CorpusMessage Newsletter { get; } = Exchange(
        1050,
        Mail("digest@harbourline.test", "Harbourline Digest", [Owner], [], At(9, 1, 6, 0), "Harbourline Monthly — September 2026", """
            Harbourline Monthly — September 2026

            The export scheduler is now available in every workspace. Reports can be set to run nightly or weekly, and each
            finished export is kept for fourteen days.

            Our support desk now answers in four languages: English, German, Polish, and Portuguese.

            Tip of the month: saved filters can be shared with a whole team from the filter menu.

            You are receiving this newsletter because you subscribed to product news from Harbourline.
            Unsubscribe at https://harbourline.test/unsubscribe
            """))[0];

    /// <summary>Gets every conversation written here, including those only a contact's correspondence reads.</summary>
    /// <remarks>Declared last, because a static property is initialised in the order it is written and this one reads every one above.</remarks>
    public static IReadOnlyList<IReadOnlyList<CorpusMessage>> Exchanges { get; } =
    [
        CommitmentWithdrawn,
        QuestionAnsweredLater,
        QualifiedAgreement,
        [Acknowledgement],
        [RequestInQuotedHistory],

        // Three issues of one newsletter, whose sender has a correspondence of nothing but them.
        Exchange(1060, Mail("digest@harbourline.test", "Harbourline Digest", [Owner], [], At(7, 1, 6, 0), "Harbourline Monthly — July 2026", "The July product notes: faster search and a new dark theme.")),
        Exchange(1061, Mail("digest@harbourline.test", "Harbourline Digest", [Owner], [], At(8, 1, 6, 0), "Harbourline Monthly — August 2026", "The August product notes: audit log export and two new integrations.")),
        [Newsletter],

        // A supplier whose tone moves from a welcome to a final notice over one invoice nobody paid.
        Exchange(1100, Mail("oskar.brandt@fernwick.test", "Oskar Brandt", [Owner], [], At(6, 3, 9, 15), "Welcome aboard — looking forward to working together", "Welcome to Fernwick Supply.")),
        Exchange(1101, Mail("oskar.brandt@fernwick.test", "Oskar Brandt", [Owner], [], At(6, 24, 14, 2), "Thanks for the quick turnaround on the first order", "Thank you for confirming the first order so quickly.")),
        Exchange(1102, Mail("oskar.brandt@fernwick.test", "Oskar Brandt", [Owner], [], At(7, 29, 8, 40), "Invoice FW-2207 is now overdue", "Invoice FW-2207 was due on 27 July.")),
        Exchange(1103, Mail("oskar.brandt@fernwick.test", "Oskar Brandt", [Owner], [], At(8, 19, 8, 35), "Second reminder: invoice FW-2207 still unpaid", "This is our second reminder about invoice FW-2207.")),
        Exchange(1104, Mail("oskar.brandt@fernwick.test", "Oskar Brandt", [Owner], [], At(9, 9, 8, 30), "Final notice: FW-2207 unpaid — account on hold from 30 September", "Unless FW-2207 is paid, the account is on hold from 30 September.")),

        // A colleague who is only ever copied, on conversations that each inform rather than ask.
        Exchange(1200, Mail("ines.carvalho@lindenrow.test", "Ines Carvalho", [Owner], ["greta.holm@lindenrow.test"], At(8, 6, 16, 10), "Minutes: Lindenrow quarterly review", "The minutes of Tuesday's quarterly review are below.")),
        Exchange(1201, Mail("ines.carvalho@lindenrow.test", "Ines Carvalho", [Owner], ["greta.holm@lindenrow.test"], At(8, 20, 11, 45), "FYI: new office address from 1 October", "From 1 October our office is at 9 Canal Row.")),
        Exchange(1202, Mail("ines.carvalho@lindenrow.test", "Ines Carvalho", [Owner], ["greta.holm@lindenrow.test"], At(9, 3, 10, 5), "For your records: signed framework agreement", "The countersigned framework agreement is filed for both sides.")),

        // A weekly report that always arrives on a Monday morning.
        Exchange(1300, Mail("priya.nair@quaymark.test", "Priya Nair", [Owner], [], At(8, 17, 8, 5), "Weekly status report — week 34", "This week's report is attached.", ["status-week-34.pdf"])),
        Exchange(1301, Mail("priya.nair@quaymark.test", "Priya Nair", [Owner], [], At(8, 24, 8, 2), "Weekly status report — week 35", "This week's report is attached.", ["status-week-35.pdf"])),
        Exchange(1302, Mail("priya.nair@quaymark.test", "Priya Nair", [Owner], [], At(8, 31, 8, 7), "Weekly status report — week 36", "This week's report is attached.", ["status-week-36.pdf"])),
        Exchange(1303, Mail("priya.nair@quaymark.test", "Priya Nair", [Owner], [], At(9, 7, 8, 4), "Weekly status report — week 37", "This week's report is attached.", ["status-week-37.pdf"])),

        // A contract negotiated through three versions of one document, the last of them signed.
        Exchange(1400, Mail("hanna.kowal@fernwick.test", "Hanna Kowal", [Owner], [], At(7, 14, 10, 20), "MSA draft for your review", "The first draft is attached.", ["msa-draft-v1.docx"])),
        Exchange(1401, Mail("hanna.kowal@fernwick.test", "Hanna Kowal", [Owner], [], At(8, 4, 15, 55), "MSA v2 with your comments addressed", "The second draft is attached.", ["msa-draft-v2.docx"])),
        Exchange(1402, Mail("hanna.kowal@fernwick.test", "Hanna Kowal", [Owner], [], At(8, 25, 9, 30), "Signed MSA attached", "The signed agreement is attached.", ["msa-signed.pdf"])),

        // A quotation asked for, answered once, and chased since.
        Exchange(
            1500,
            Mail(Owner, "Mara", ["emil.varga@birchline.test"], [], At(8, 10, 9, 0), "Quote request: 40 office chairs", "Could you quote 40 task chairs?"),
            Mail("emil.varga@birchline.test", "Emil Varga", [Owner], [], At(8, 11, 13, 30), "Re: Quote request: 40 office chairs", "Pricing will follow shortly.")),
        Exchange(1502, Mail(Owner, "Mara", ["emil.varga@birchline.test"], [], At(9, 1, 9, 10), "Still waiting on the chair quote", "Is the chair quote ready?")),

        // A trip arranged and then finalised, which leaves nothing to do.
        Exchange(1600, Mail("nadia.rossi@birchline.test", "Nadia Rossi", [Owner], [], At(8, 26, 12, 0), "Flight booked: Lisbon, 12 October", "Your flight is booked.")),
        Exchange(1601, Mail("nadia.rossi@birchline.test", "Nadia Rossi", [Owner], [], At(8, 27, 16, 45), "Hotel confirmed — Lisbon, 12–15 October", "Your hotel is confirmed.")),
        Exchange(1602, Mail("nadia.rossi@birchline.test", "Nadia Rossi", [Owner], [], At(9, 2, 11, 30), "Your Lisbon itinerary is final", "Everything for the Lisbon trip is final.")),
    ];

    private static DateTimeOffset At(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static WrittenMessage Mail(
        string from,
        string fromName,
        string[] to,
        string[] cc,
        DateTimeOffset at,
        string subject,
        string body,
        string[]? attachments = null) =>
        new(from, fromName, to, cc, at, subject, body, attachments);

    private static IReadOnlyList<CorpusMessage> Exchange(int firstPosition, params WrittenMessage[] messages) =>
        [.. messages.Select((message, offset) => CorpusMessage.Of(message.Compose(), firstPosition + offset))];

    /// <summary>One message as it is written here, before it is composed into the MIME a deployment would have stored.</summary>
    private sealed record WrittenMessage(
        string From,
        string FromName,
        string[] To,
        string[] Cc,
        DateTimeOffset At,
        string Subject,
        string Body,
        string[]? Attachments = null)
    {
        public MimeMessage Compose()
        {
            var message = new MimeMessage
            {
                Date = this.At,
                Subject = this.Subject,
            };

            message.From.Add(new MailboxAddress(this.FromName, this.From));
            message.To.AddRange(this.To.Select(static address => MailboxAddress.Parse(address)));
            message.Cc.AddRange(this.Cc.Select(static address => MailboxAddress.Parse(address)));

            var body = new BodyBuilder { TextBody = this.Body };

            foreach (var fileName in this.Attachments ?? [])
            {
                body.Attachments.Add(fileName, [0x25, 0x50, 0x44, 0x46], ContentType.Parse(MimeTypes.GetMimeType(fileName)));
            }

            message.Body = body.ToMessageBody();

            return message;
        }
    }
}
