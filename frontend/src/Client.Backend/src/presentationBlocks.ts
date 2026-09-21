// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { asRecord } from './json';

// What the blocks of a presentation plan carry, and the sources they rest on. It sits apart from `discoveryRun.ts`
// because that module is the route — when a run is read, what a cursor means, how a tail is asked for — and this is
// the document the route answers with. Nine block types will land here one at a time, and a route module growing a
// parser per type is a module that stops being about the route.
//
// **Everything here is read from untrusted input at the trust boundary**, so every bound the service composes a plan
// under is checked again on the way in rather than assumed: a plan is written by a model's output passing through the
// service's own refusals, and a client that trusted the second of those would be trusting the first.
//
// **Nothing here decides what a block looks like.** A verdict, a confidence, and a freshness each arrive as the value
// the contract closed them over, and what each is called in a language — and which of them is drawn in a warning
// tint — is the screen's.

/** What the correspondence does for a block, which is the contract's own four-valued verdict. */
export type BlockSupport = 'Supported' | 'Unsupported' | 'Conflicting' | 'Stale';

const supports: readonly BlockSupport[] = ['Supported', 'Unsupported', 'Conflicting', 'Stale'];

/** How far an answer is worth trusting beyond the sources it names, as the band the composition capped it to. */
export type AnswerConfidence = 'High' | 'Moderate' | 'Low';

const confidences: readonly AnswerConfidence[] = ['High', 'Moderate', 'Low'];

/** Whether the local copy a block rests on was current, was known to be behind the mail server, or was never established. */
export type SourceStaleness = 'Current' | 'Stale' | 'Unknown';

const stalenesses: readonly SourceStaleness[] = ['Current', 'Stale', 'Unknown'];

/** Whether somebody wrote the source or this deployment described a picture of it. */
export type SourceMedium = 'Written' | 'Depicted';

const mediums: readonly SourceMedium[] = ['Written', 'Depicted'];

/** Why a declared source yielded nothing, which is a state a reader acts on rather than a failure to report. */
export type UnreadableSource =
    'Encrypted' | 'Corrupt' | 'FormatNotRead' | 'TooLarge' | 'NoTextFound' | 'NotAttempted' | 'ReadingFailed';

const unreadables: readonly UnreadableSource[] = [
    'Encrypted',
    'Corrupt',
    'FormatNotRead',
    'TooLarge',
    'NoTextFound',
    'NotAttempted',
    'ReadingFailed',
];

/** What a citation points at: the message itself, one persisted passage of it, or one of its attachments. */
export type CitedSourceKind = 'email' | 'fragment' | 'attachment';

const sourceKinds: readonly CitedSourceKind[] = ['email', 'fragment', 'attachment'];

/** How current the local copy behind a block or an entry was, and when that was established. */
export interface SourceFreshness {
    readonly staleness: SourceStaleness;

    /** When the local copy was established, and `null` where nothing established it. */
    readonly observedAt: string | null;
}

/** One side of a disagreement between sources: what that part of the correspondence says, and which sources say it. */
export interface ConflictingClaim {
    readonly statement: string;

    /** The sources saying it, which are sources the block itself rests on. */
    readonly sources: readonly string[];
}

/** What the correspondence does for one block. */
export interface BlockEvidence {
    readonly support: BlockSupport;

    /** The sources the block rests on, in the order they are worth reading, and empty for an unsupported block. */
    readonly citations: readonly string[];

    readonly freshness: SourceFreshness;

    /** The sides of the disagreement, which only a conflicting block carries. */
    readonly conflictingClaims: readonly ConflictingClaim[];
}

/** One source a run's blocks rest on, declared before anything names it. */
export interface DeclaredSource {
    /** The name the blocks refer to this source by. */
    readonly id: string;

    /** What it points at, or `null` where the run named a kind this contract does not carry. */
    readonly kind: CitedSourceKind | null;

    /** What the source is called, which is what a screen puts on the citation rather than the identifier. */
    readonly label: string;

    readonly medium: SourceMedium;

    /** Why the source yielded nothing, and `null` where it was read. */
    readonly unreadable: UnreadableSource | null;
}

/** One synthesized answer, in the words the run wrote it in. */
export interface SynthesizedAnswer {
    readonly text: string;
    readonly confidence: AnswerConfidence;
}

/** One message an answer rests on, and the part of it worth reading. */
export interface EvidenceEntry {
    /** The source the entry presents, named as the run declared it. */
    readonly source: string;

    /** The part of the message worth reading, quoted from it. */
    readonly fragment: string;

    /** How well the entry answers the question, between `0` and `1` inclusive. */
    readonly relevance: number;

    readonly freshness: SourceFreshness;
}

// The service's own bounds, restated because this side of the boundary refuses what it will not draw rather than
// trusting the side that composed it. Each is the constant the contract names: a block rests on at most twenty-four
// sources, presents at most six sides of a disagreement, lists at most fifty messages, and every free text it carries
// is one presentation text.
const mostCitations = 24;
const mostConflictingClaims = 6;
const mostEvidenceEntries = 50;
const longestText = 4000;
const longestCitationId = 32;

/** What the correspondence does for one block, or `null` where what arrived is not evidence this client can read. */
export function parseBlockEvidence(value: unknown): BlockEvidence | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const support = oneOf(record['support'], supports);
    const citations = parseCitationIds(record['citations'], mostCitations);
    const freshness = parseFreshness(record['freshness']);

    if (support === null || citations === null || freshness === null) {
        return null;
    }

    const conflictingClaims = parseConflictingClaims(record['conflictingClaims']);

    return conflictingClaims === null ? null : { support, citations, freshness, conflictingClaims };
}

/** One source a run declared, or `null` where what arrived is not a declaration this client can read. */
export function parseDeclaredSource(value: unknown): DeclaredSource | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = parseCitationId(record['id']);
    const label = parseText(record['label']);
    const medium = oneOf(record['medium'], mediums);
    const target = asRecord(record['target']);

    if (id === null || label === null || medium === null || target === null) {
        return null;
    }

    // An unreadable reason that is absent and one that is present as nothing are the same fact: the journal writes no
    // member at all and the route that serves it writes `null`, and a source that was read carries neither.
    const declared = record['unreadable'];
    const unreadable = declared === undefined || declared === null ? null : oneOf(declared, unreadables);

    if (declared !== undefined && declared !== null && unreadable === null) {
        return null;
    }

    // The kind is read and the identity it names is not, because nothing draws a source by its identity yet: following
    // a citation is the evidence inspector's, and the members it takes are read in the change that follows one.
    const kind = typeof target['kind'] === 'string' ? oneOf(target['kind'], sourceKinds) : null;

    return { id, kind, label, medium, unreadable };
}

/** One synthesized answer, or `null` where what arrived is not one this client can draw. */
export function parseSynthesizedAnswer(value: unknown): SynthesizedAnswer | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const text = parseText(record['text']);
    const confidence = oneOf(record['confidence'], confidences);

    return text === null || confidence === null ? null : { text, confidence };
}

/**
 * The messages a block lists, or `null` where what arrived is not a list this client can draw.
 *
 * A list holding nothing is read rather than refused: the contract composes none, and an answer that drew an error
 * over a list which arrived empty would be reporting a defect where the honest reading is that there was nothing in it.
 */
export function parseEvidenceEntries(value: unknown): readonly EvidenceEntry[] | null {
    if (!Array.isArray(value) || value.length > mostEvidenceEntries) {
        return null;
    }

    const entries: EvidenceEntry[] = [];
    for (const written of value) {
        const entry = parseEvidenceEntry(written);
        if (entry === null) {
            return null;
        }

        entries.push(entry);
    }

    return entries;
}

function parseEvidenceEntry(value: unknown): EvidenceEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const source = parseCitationId(record['source']);
    const fragment = parseText(record['fragment']);
    const relevance = record['relevance'];
    const freshness = parseFreshness(record['freshness']);

    if (source === null || fragment === null || freshness === null) {
        return null;
    }

    return typeof relevance === 'number' && Number.isFinite(relevance) && relevance >= 0 && relevance <= 1
        ? { source, fragment, relevance, freshness }
        : null;
}

function parseConflictingClaims(value: unknown): readonly ConflictingClaim[] | null {
    // The member is absent on a block that carries none, which every block but a conflicting one is.
    if (value === undefined || value === null) {
        return [];
    }

    if (!Array.isArray(value) || value.length > mostConflictingClaims) {
        return null;
    }

    const claims: ConflictingClaim[] = [];
    for (const written of value) {
        const record = asRecord(written);
        const statement = record === null ? null : parseText(record['statement']);
        const sources = record === null ? null : parseCitationIds(record['sources'], mostCitations);

        if (statement === null || sources === null || sources.length === 0) {
            return null;
        }

        claims.push({ statement, sources });
    }

    return claims;
}

function parseFreshness(value: unknown): SourceFreshness | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const staleness = oneOf(record['staleness'], stalenesses);
    if (staleness === null) {
        return null;
    }

    const observedAt = record['observedAt'];
    if (observedAt === undefined || observedAt === null) {
        return { staleness, observedAt: null };
    }

    return typeof observedAt === 'string' && observedAt.length > 0 && observedAt.length <= longestText
        ? { staleness, observedAt }
        : null;
}

function parseCitationIds(value: unknown, most: number): readonly string[] | null {
    if (!Array.isArray(value) || value.length > most) {
        return null;
    }

    const ids: string[] = [];
    for (const written of value) {
        const id = parseCitationId(written);
        if (id === null) {
            return null;
        }

        ids.push(id);
    }

    return ids;
}

function parseCitationId(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestCitationId ? value : null;
}

function parseText(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestText ? value : null;
}

function oneOf<TValue extends string>(value: unknown, values: readonly TValue[]): TValue | null {
    return typeof value === 'string' && (values as readonly string[]).includes(value) ? (value as TValue) : null;
}
