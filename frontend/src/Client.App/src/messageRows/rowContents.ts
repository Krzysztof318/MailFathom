// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailEnrichmentAspect, MailTimelineEntry, SignalledFlags } from '@mailfathom/client-backend';
import { leadingReading } from './messageReadings';

// What a row draws, as one value two answers about the same message can be compared by. It is what decides whether a
// reader is shown a row changing, and it sits beside the row rather than beside the list that animates one: what makes
// a row *changed* is that something the row draws is different, so a list comparing whichever fields it happened to
// think of would wash rows the reader sees no difference in and — worse the other way round — would miss a field
// somebody later drew.
//
// **The question is about what is drawn and not about what arrived.** A page read again carries a whole answer per
// message, and a deployment that re-derived a reading, re-counted a passage, or merely wrote the record again answers
// with the same row. So a refresh that turns up nothing new washes nothing, which is the whole point: the list is drawn
// once and every change after it reaches the rows it actually touched.

/**
 * Everything a row draws about one message, and nothing else.
 *
 * A mark rather than the answer it was taken from, because what holds it is a map with one entry per row the reader has
 * been shown: the list bounds that map at ten thousand rows, and holding the answers themselves there would be holding
 * a mailbox in memory to answer a question about a dozen of their fields.
 *
 * The fields are what `messageRows/MessageRow.tsx` draws, in the order it draws them: who wrote and where from, the
 * marks, whether it is unread, how long the correspondence is, when it arrived, the subject, and the leading reading in
 * the line the row reserves. The ones the wire carries and no row draws are left out deliberately rather than
 * forgotten — the account and folder it was answered in, the instant it was sent, everyone it was addressed to, its
 * size, and the preview. A message whose preview was re-derived is the same row, and washing it would be the client
 * marking a change nobody can see.
 */
export interface RowContents {
    readonly senderDisplayName: string | null;
    readonly senderAddress: string | null;
    readonly subject: string | null;
    readonly receivedAt: string | null;
    readonly unread: boolean;
    readonly flagged: boolean;
    readonly answered: boolean;
    readonly hasAttachments: boolean;
    readonly attachmentCount: number;
    readonly threadMessageCount: number | null;

    /**
     * The reading the row's reserved line says, or `null` where the message carries none.
     *
     * The aspect and the words alone, because those are the two `messageRows/MessageReading.tsx` puts on the line: the
     * reason it gives, when it is owed, and what produced it are the surface the row's menu opens. A derivation that
     * added a second mark the row does not show is not a row that changed.
     */
    readonly reading: { readonly aspect: MailEnrichmentAspect; readonly text: string } | null;
}

/** What a row draws, out of the answer the page carried about it. */
export function rowMark(email: MailTimelineEntry): RowContents {
    const reading = leadingReading(email.enrichment);

    return {
        senderDisplayName: email.senderDisplayName,
        senderAddress: email.senderAddress,
        subject: email.subject,
        receivedAt: email.receivedAt,
        unread: email.unread,
        flagged: email.flagged,
        answered: email.answered,
        hasAttachments: email.hasAttachments,
        attachmentCount: email.attachmentCount,
        threadMessageCount: email.threadMessageCount,
        reading: reading === null ? null : { aspect: reading.aspect, text: reading.text },
    };
}

/** Whether a reader looking at the row would see no difference between the two. */
export function sameRow(one: RowContents, other: RowContents): boolean {
    return (
        one.senderDisplayName === other.senderDisplayName &&
        one.senderAddress === other.senderAddress &&
        one.subject === other.subject &&
        one.receivedAt === other.receivedAt &&
        one.unread === other.unread &&
        one.flagged === other.flagged &&
        one.answered === other.answered &&
        one.hasAttachments === other.hasAttachments &&
        one.attachmentCount === other.attachmentCount &&
        one.threadMessageCount === other.threadMessageCount &&
        one.reading?.aspect === other.reading?.aspect &&
        (one.reading?.text ?? null) === (other.reading?.text ?? null)
    );
}

/**
 * The mark a row carries once the deployment has said where its flags stand.
 *
 * The one change the list applies to a row in place rather than by reading its page again, so it is the one change the
 * mark has to be told about outside a page answering: a mark left behind would make the next read of that page differ
 * from it and wash a row the reader was already shown changing.
 *
 * A flag the statement is silent about is one the deployment did not observe rather than one it cleared, which is the
 * reading `heldTimeline.ts` applies to the row itself — stated once in each place because the row and its mark have to
 * come out the same.
 */
export function rowWithFlags(mark: RowContents, stated: SignalledFlags): RowContents {
    return {
        ...mark,
        unread: stated.isSeen === null ? mark.unread : !stated.isSeen,
        flagged: stated.isFlagged ?? mark.flagged,
    };
}
