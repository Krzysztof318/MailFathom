// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    MailEnrichment,
    MailEnrichmentAspect,
    MailEnrichmentMark,
    MailEnrichmentSource,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';

// Which of a message's readings a row draws, and in what order every one of them is read when somebody opens them.
//
// It is arithmetic over what the deployment already decided rather than a judgement of its own, and it is a module
// because both surfaces need the same answer: the row draws one reading and the panel behind it draws all of them, and
// a second ordering written into either is how the sentence on the row would stop being the first one in the panel.
//
// **A row draws one reading and never three.** The row's height is the token's rather than its contents', which is
// what the window above it is arithmetic over, so what the reserved line holds is one sentence — and choosing which
// is a decision rather than a default: a folder is scanned for what is owed, so a commitment leads, then why the
// message matters, then what it is about. That order is also why a message with all three is not a taller row.

/**
 * Every reading, most actionable first.
 *
 * A commitment is the one that carries a date and the one that costs somebody something if it is missed, so it leads;
 * the significance says why the message matters at all; the sense says what it is, which is the reading a subject line
 * already half-answers.
 */
export const readingOrder: readonly MailEnrichmentAspect[] = ['Commitment', 'Significance', 'Sense'];

/**
 * What each reading is called, which is what says a sentence on a row is a reading of the message rather than from it.
 *
 * Exhaustive by the deployment's own closed set rather than a lookup with a fallback: an aspect this client has no
 * name for is a compiler error here, which is where it can still be answered, rather than an unlabelled sentence on
 * somebody's list.
 */
export const readingNames: Readonly<Record<MailEnrichmentAspect, MessageKey>> = {
    Sense: 'reading.sense',
    Significance: 'reading.significance',
    Commitment: 'reading.commitment',
};

/**
 * What each producer is called, which is the distinction a reader weighs before they read the sentence.
 *
 * A deterministic rule re-run over the same message says the same thing and a model promises no such thing, so the two
 * are named apart and drawn apart. Nothing here collapses them into "AI".
 */
export const readingSources: Readonly<Record<MailEnrichmentSource, MessageKey>> = {
    DeterministicRule: 'reading.byRule',
    Model: 'reading.byModel',
};

/**
 * The readings of one message, most actionable first.
 *
 * @param enrichment What a derivation concluded, or `null` where none has reached the message.
 * @returns The marks in reading order, which is empty for a message with nothing to say and for one never derived.
 */
export function readingsOf(enrichment: MailEnrichment | null): readonly MailEnrichmentMark[] {
    if (enrichment === null) {
        return [];
    }

    // Sorted from the aspect rather than filtered per aspect, because the deployment publishes at most one mark of
    // each: a message carrying two of one aspect is refused at the boundary, so there is no choosing to do here.
    return [...enrichment.marks].sort((first, second) => positionOf(first.aspect) - positionOf(second.aspect));
}

/**
 * The reading a row draws, or `null` where the row draws none.
 *
 * @param enrichment What a derivation concluded, or `null` where none has reached the message.
 * @returns The leading mark, or `null` for a message no reading was written for.
 */
export function leadingReading(enrichment: MailEnrichment | null): MailEnrichmentMark | null {
    return readingsOf(enrichment)[0] ?? null;
}

function positionOf(aspect: MailEnrichmentAspect): number {
    const at = readingOrder.indexOf(aspect);

    // An aspect this order does not name sorts last rather than first: the closed set is the deployment's and a
    // member added to it is one this client has not been told how to rank, which is not a reason to lead with it.
    return at === -1 ? readingOrder.length : at;
}
