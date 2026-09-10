// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    MailBody,
    MailDraftAnswer,
    MailDraftComposition,
    MailMessage,
    MailParticipant,
} from '@mailfathom/client-backend';
import { draftWords } from './draftWords';
import { htmlOf, plainTextOf, type WrittenNode } from './writtenText';

// What somebody is writing, as values rather than as anything on the screen. The composer draws it, the confirmation
// reads it, and everything below is the arithmetic between: what an answer opens addressed to, what a subject reads as,
// and what a send would go out without.
//
// **Deriving an answer's recipients is composition rather than a rule.** The deployment decides who a message may be
// sent to and this decides nobody: what is composed here is the ordinary opening of a reply, which the author then sees
// and edits before anything is saved. The account, the subject, and the threading identifiers of an answer are the
// deployment's and are never sent from here — `wireComposition` is where that shows.

/**
 * What opening the composer is: a message of its own, an answer to one this deployment holds, or a draft already in a
 * drafts folder being carried on with.
 */
export type ComposerOpening =
    | { readonly kind: 'new' }
    | { readonly kind: 'answer'; readonly answers: MailDraftAnswer; readonly storedEmailId: string }
    | { readonly kind: 'draft'; readonly storedEmailId: string };

/** What the author has written, and what it is being written against. */
export interface Composition {
    /** The message this answers and which answer it is, or `null` for a message of its own. */
    readonly answering: { readonly storedEmailId: string; readonly answers: MailDraftAnswer } | null;

    /**
     * The message in a drafts folder this was opened from, or `null` where it was not opened from one.
     *
     * It says where the words came from and nothing else: a save still writes a draft of its own, because the client
     * surface publishes no route that reads a draft back or takes a stored message over as the one being written. What
     * it is for is telling one composition from another — a message of its own and a draft carried on with both answer
     * nothing, so without it the tab would restore whichever of the two it kept last into either.
     */
    readonly continuing: string | null;

    /** The account it goes out as, which an answer reads from the message it answers and therefore never states. */
    readonly account: string;

    readonly subject: string;
    readonly to: readonly string[];
    readonly cc: readonly string[];
    readonly bcc: readonly string[];

    /**
     * The words themselves, which is the one thing here nothing but the author writes.
     *
     * They are a tree rather than a string because the composer writes rich text, and one tree rather than a pair
     * because the two parts a message goes out with are both read from it: a plain-text alternative kept beside the
     * markup would be a second copy of the same message, and a pair that can disagree is two readers being told
     * different things.
     */
    readonly words: readonly WrittenNode[];
}

/**
 * What a send would go out without, which the confirmation names before it goes.
 *
 * None of the three refuses the send: a message with no subject is a message somebody meant to send that way often
 * enough, and this is the moment to notice rather than the moment to be stopped.
 */
export type SendCaution = 'noRecipient' | 'noSubject' | 'noWords';

/** The most addresses the composer takes in one header, which is what the deployment answers a draft with at most. */
export const mostRecipientsInOneHeader = 256;

/** A message of its own, addressed to nobody and about nothing yet. */
export function nothingWrittenYet(account: string): Composition {
    return { answering: null, continuing: null, account, subject: '', to: [], cc: [], bcc: [], words: [] };
}

/**
 * The answer somebody has just asked to write, opened the way a mail client opens one.
 *
 * @param message The message being answered, as the deployment described it.
 * @param answers Which answer is being written.
 */
export function answerTo(message: MailMessage, answers: MailDraftAnswer): Composition {
    const authors = addressesOf(message.headers.participants, ['ReplyTo']);
    const to = authors.length > 0 ? authors : addressesOf(message.headers.participants, ['From', 'Sender']);
    const everybody = addressesOf(message.headers.participants, ['To', 'Cc']);

    return {
        answering: { storedEmailId: message.storedEmailId, answers },
        continuing: null,
        account: message.account,
        subject: answeredSubject(message.headers.subject, answers),

        // A forward is addressed by the person forwarding it: what it carries is somebody else's conversation, and
        // opening it addressed to the people already in that conversation is how a private thread reaches them twice.
        to: answers === 'forward' ? [] : to,
        cc: answers === 'everyone' ? everybody.filter((address) => !to.includes(address)) : [],
        bcc: [],
        words: [],
    };
}

/**
 * The draft somebody has just opened, put back into the composer that writes one.
 *
 * A draft is a message this deployment already holds, so nothing here is composed: the account, the addresses, the
 * subject, and the words are read back exactly as they were filed. What it is not is the deployment's own draft
 * record — the client surface publishes no route that reads one back — so `continuing` says which stored message the
 * words came from and a save still writes a draft of its own beside it.
 *
 * @param message The draft being carried on with, as the deployment described it.
 * @param body Its body as the deployment reduced it, or `null` where it could not be read.
 */
export function draftContinued(message: MailMessage, body: MailBody | null): Composition {
    return {
        answering: null,
        continuing: message.storedEmailId,
        account: message.account,
        subject: message.headers.subject ?? '',
        to: addressesOf(message.headers.participants, ['To']),
        cc: addressesOf(message.headers.participants, ['Cc']),
        bcc: addressesOf(message.headers.participants, ['Bcc']),
        words: body === null ? [] : draftWords(body),
    };
}

/**
 * What the subject of an answer reads as.
 *
 * It is shown rather than sent: the deployment derives an answer's subject from the message it answers, so what this
 * composes is what the author is about to reply under rather than a value the client decides.
 */
export function answeredSubject(subject: string | null, answers: MailDraftAnswer): string {
    const written = subject ?? '';
    const prefix = answers === 'forward' ? 'Fwd: ' : 'Re: ';

    // A reply to a reply is still one message about one thing, so the prefix is not stacked. Matched without regard to
    // case because a mail client somewhere writes each of the two spellings.
    return written.toLowerCase().startsWith(prefix.toLowerCase()) ? written : `${prefix}${written}`;
}

/** What a send would go out without, in the order somebody reads them. */
export function whatWouldBeMissing(composition: Composition): readonly SendCaution[] {
    const missing: SendCaution[] = [];

    if (composition.to.length === 0 && composition.cc.length === 0 && composition.bcc.length === 0) {
        missing.push('noRecipient');
    }

    if (composition.subject.trim() === '') {
        missing.push('noSubject');
    }

    if (plainTextOf(composition.words).trim() === '') {
        missing.push('noWords');
    }

    return missing;
}

/** Whether anything has been written that closing the composer would throw away. */
export function anythingWritten(composition: Composition): boolean {
    return (
        plainTextOf(composition.words).trim() !== '' ||
        composition.to.length > 0 ||
        composition.cc.length > 0 ||
        composition.bcc.length > 0
    );
}

/**
 * What a save states on the wire.
 *
 * An answer names the message it answers and neither an account nor a subject, because those are the deployment's to
 * derive — which is what keeps an edited reply a reply rather than a new message with a similar subject.
 */
export function wireComposition(composition: Composition): MailDraftComposition {
    const plainText = plainTextOf(composition.words);

    const written = {
        plainTextBody: plainText,

        // Both parts, or neither: a message with nothing written in it states no HTML alternative to nothing, and one
        // with words in it carries the markup its author gave them beside the reading every mail client has.
        htmlBody: plainText === '' ? null : htmlOf(composition.words),
        to: composition.to,
        cc: composition.cc,
        bcc: composition.bcc,
    };

    return composition.answering === null
        ? { ...written, account: composition.account, subject: composition.subject }
        : {
              ...written,
              answeredEmailId: composition.answering.storedEmailId,
              answers: composition.answering.answers,
          };
}

/**
 * Whether the text is shaped like an address at all.
 *
 * The deployment is what decides whether an address may be written to and whether it exists; this is the smaller
 * question of whether somebody has finished typing one, so that a chip is not made out of half a name.
 */
export function looksLikeAnAddress(text: string): boolean {
    const [local, domain, ...rest] = text.split('@');

    return (
        rest.length === 0 &&
        local !== undefined &&
        domain !== undefined &&
        local.length > 0 &&
        domain.length > 0 &&
        !/\s/u.test(text)
    );
}

/** The addresses written in any of the named headers, in the order they were written and without a repeat. */
function addressesOf(
    participants: readonly MailParticipant[],
    roles: readonly MailParticipant['role'][],
): readonly string[] {
    const found: string[] = [];

    for (const participant of participants) {
        if (roles.includes(participant.role) && !found.includes(participant.address)) {
            found.push(participant.address);
        }
    }

    return found;
}
