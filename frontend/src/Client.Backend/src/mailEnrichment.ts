// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { asRecord } from './json';

// What a derivation concluded about one message, which the deployment writes down once when the message arrives and
// then publishes on every row that draws that message. It is its own module rather than three more fields inside
// `mailTimeline.ts` because two things reach for it: the row parser every mail surface shares, and the reader that
// follows a mark's evidence back to the passages it rests on.
//
// **The two empty answers are different answers.** A message no derivation has reached carries `null`; one a
// derivation settled with nothing to say carries an object whose `marks` is empty. Nothing here collapses them,
// because a deployment with enrichment switched off is the first of the two and a message there is nothing to say
// about is the second, and a screen that drew them alike would still have been told which it was.

/** Which of the three readings a mark carries, named as the deployment publishes it. */
export type MailEnrichmentAspect = 'Sense' | 'Significance' | 'Commitment';

/** What produced a mark, which is the distinction somebody weighing it asks about before they read it. */
export type MailEnrichmentSource = 'DeterministicRule' | 'Model';

const aspects: readonly MailEnrichmentAspect[] = ['Sense', 'Significance', 'Commitment'];

const sources: readonly MailEnrichmentSource[] = ['DeterministicRule', 'Model'];

// What one mark may carry before the row it is on is refused. Each is the deployment's own bound rather than a guess:
// the sentence and the reason are cut to 240 characters where they are stored, a message carries at most one mark of
// each aspect, and a mark rests on at most four passages. A row past any of them is an answer this surface does not
// produce, and drawing it would be drawing something other than a derivation.
const longestMarkText = 240;
const longestOrigin = 128;
const mostMarks = 3;
const mostEvidence = 4;
const longestPassageIdentity = 256;

/** One reading of a message, with what backs it and what produced it. */
export interface MailEnrichmentMark {
    readonly aspect: MailEnrichmentAspect;

    /** The reading itself, as one sentence, which is what a row draws. */
    readonly text: string;

    /** Why the producer says it, which is what somebody checks the reading against. */
    readonly reason: string;

    /** When the commitment falls due, or `null` on every other aspect and on a commitment that named no date. */
    readonly dueAt: string | null;

    readonly source: MailEnrichmentSource;

    /** What within that source produced it: a rule identity, or the name the agent was composed under. */
    readonly origin: string;

    /**
     * The passages the reading rests on, in the order the producer named them.
     *
     * Identifiers rather than text, because the deployment publishes them that way: a list page carrying the passages
     * themselves would be publishing a body it had no reason to. Following one is a request of its own — see
     * {@link readCitedPassages}.
     */
    readonly evidence: readonly string[];
}

/** What a derivation concluded about one message. */
export interface MailEnrichment {
    /** When the derivation ran. */
    readonly derivedAt: string;

    /** What it concluded, which is empty where it settled with nothing to say. */
    readonly marks: readonly MailEnrichmentMark[];
}

/**
 * What reading a row's derivation produced: the value, which may legitimately be `null`.
 *
 * A wrapper rather than a bare `MailEnrichment | null`, because `null` is one of the two states this field publishes
 * and is therefore not available to mean *refused*. Every other parser in this package can say "no" with `null`; this
 * one cannot, and returning the same shape from both answers would hand a screen a row whose derivation was silently
 * dropped rather than refusing the page.
 */
export interface ParsedEnrichment {
    readonly enrichment: MailEnrichment | null;
}

/**
 * Reads what a row says about its message's derivation.
 *
 * @param value The field as the deployment sent it, which every row carries and no row omits.
 * @returns The derivation or its absence, or `null` where the field is neither.
 */
export function parseEnrichment(value: unknown): ParsedEnrichment | null {
    if (value === undefined || value === null) {
        return { enrichment: null };
    }

    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const derivedAt = record['derivedAt'];
    const marks = record['marks'];

    if (typeof derivedAt !== 'string' || derivedAt.length === 0 || derivedAt.length > longestMarkText) {
        return null;
    }

    if (!Array.isArray(marks) || marks.length > mostMarks) {
        return null;
    }

    const read: MailEnrichmentMark[] = [];
    for (const mark of marks) {
        const parsed = parseMark(mark);
        if (parsed === null) {
            return null;
        }

        read.push(parsed);
    }

    // A message carries at most one mark of each aspect, which is what lets a row draw a reading without choosing
    // between two answers to one question. A page answering twice for one aspect is refused rather than reconciled.
    if (new Set(read.map((mark) => mark.aspect)).size !== read.length) {
        return null;
    }

    return { enrichment: { derivedAt, marks: read } };
}

function parseMark(value: unknown): MailEnrichmentMark | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const aspect = record['aspect'];
    const text = record['text'];
    const reason = record['reason'];
    const dueAt = record['dueAt'] ?? null;
    const source = record['source'];
    const origin = record['origin'];

    if (!isAspect(aspect) || !isSource(source)) {
        return null;
    }

    if (!isSentence(text) || !isSentence(reason)) {
        return null;
    }

    if (typeof origin !== 'string' || origin.length === 0 || origin.length > longestOrigin) {
        return null;
    }

    if (dueAt !== null && (typeof dueAt !== 'string' || dueAt.length === 0 || dueAt.length > longestMarkText)) {
        return null;
    }

    // A date on any aspect but a commitment is an answer the deployment refuses to store, so a row carrying one is not
    // a row this client draws: the mark would say something about a due date on a reading that has none.
    if (dueAt !== null && aspect !== 'Commitment') {
        return null;
    }

    const evidence = parseEvidence(record['evidence']);
    if (evidence === null) {
        return null;
    }

    return { aspect, text, reason, dueAt, source, origin, evidence };
}

function parseEvidence(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostEvidence) {
        return null;
    }

    const passages: string[] = [];
    for (const passage of value) {
        if (typeof passage !== 'string' || passage.length === 0 || passage.length > longestPassageIdentity) {
            return null;
        }

        passages.push(passage);
    }

    return passages;
}

function isAspect(value: unknown): value is MailEnrichmentAspect {
    return typeof value === 'string' && aspects.includes(value as MailEnrichmentAspect);
}

function isSource(value: unknown): value is MailEnrichmentSource {
    return typeof value === 'string' && sources.includes(value as MailEnrichmentSource);
}

// A reading with nothing in it is refused rather than drawn as an empty line: the deployment stores no such mark, and
// a row drawing one would reserve the space for a sentence and then say nothing in it.
function isSentence(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestMarkText;
}
