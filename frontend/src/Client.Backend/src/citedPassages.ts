// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { parseAttachment, type MailAttachment } from './mailMessage';
import type { CitationTarget } from './presentationBlocks';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// Following a citation back to the mail it rests on. The deployment publishes evidence as identifiers rather than as
// text — a list page carrying the passages themselves would be publishing a body it had no reason to — so a screen
// that wants to show somebody what a reading was drawn from asks for it, which is this.
//
// Two callers ask the one route, for the two things a citation can be. A mark on a message names a passage of that
// message, which is {@link readCitedPassages}; a presentation plan names any of the three targets it declares a source
// under, which is {@link readCitations}. Each parses what its own caller draws — the first the words alone, the second
// the message a source stands in as well — and the request, the bounds, and what a status means are shared, because
// those are the route's and not either caller's.
//
// **The three outcomes are three states and none of them is a failure.** A passage resolves to its words; a passage
// the message has been re-cut since resolves to nothing while the message stays; and a message the caller may not read
// resolves to nothing at all. A reader owes the difference between "this reading rests on a passage that has since
// moved" and "this reading has nothing behind it", and folding the three into an absence would take it away.

/** The route a citation is followed at, relative to the client prefix. */
export const citedPassagesRoute = '/citations/resolution';

/** What became of one citation, as the deployment names it. */
export type CitedPassageOutcome = 'Resolved' | 'Unresolvable' | 'PrivateSource';

const outcomes: readonly CitedPassageOutcome[] = ['Resolved', 'Unresolvable', 'PrivateSource'];

/**
 * How many citations one request follows, which is the deployment's own bound rather than a preference.
 *
 * A request naming more is refused with a `400`, so the client holds the same number and asks within it. It is below
 * what one message can name rather than above it: a message carries at most three marks and a mark rests on at most
 * four passages, so twelve distinct passages are reachable and the first ten of them — after the marks' own repeats
 * are folded together — are what a caller may follow. A caller that means to draw the rest says so in its own words
 * rather than asking twice; `Client.App/src/messageRows/ReadingsAsked.tsx` is the one that does.
 */
export const mostCitedPassages = 10;

// A resolved passage is one chunk of a message's text, which chunking already cut to a fixed length, and ten of them
// with their offsets and identities is what an answer holds. Well above that and far below the transport's backstop.
const longestCitationAnswer = 128 * 1024;

const longestIdentity = 256;
const longestPassage = 8_192;

// What a message's own values may be, which is the bound the message route already reads a subject and a date under.
const longestText = 4_096;

/** One passage a mark's evidence names, and what following it produced. */
export interface CitedPassage {
    /** The passage the citation named, which is what pairs a resolution with the evidence entry that asked for it. */
    readonly passage: string;

    readonly outcome: CitedPassageOutcome;

    /**
     * Where in the message's text the passage sits, counted from the start of it.
     *
     * `null` for either outcome that resolved to no passage, so a screen never draws a position for words it does not
     * have.
     */
    readonly ordinal: number | null;

    /** The words the passage holds, or `null` where the citation resolved to no passage. */
    readonly text: string | null;
}

/**
 * Follows the passages one message's evidence names, answering an expected failure as a value rather than by throwing.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param storedEmailId The message every passage belongs to, which every citation names.
 * @param passages The passages to follow, at most {@link mostCitedPassages} of them.
 * @returns One resolution per passage in the order they were named, or why none arrived.
 */
export function readCitedPassages(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
    passages: readonly string[],
): Promise<ClientResult<readonly CitedPassage[]>> {
    return spanned(`POST ${citedPassagesRoute}`, async () => {
        const answered = await askForResolutions(
            session,
            transport,
            passages.map((passage) => ({ kind: 'fragment', email: storedEmailId, fragment: passage })),
        );

        if (answered.outcome === 'failed') {
            return answered;
        }

        const resolved: CitedPassage[] = [];
        for (const [at, citation] of answered.value.entries()) {
            const passage = passages[at];
            const one = passage === undefined ? null : parsePassageResolution(citation, passage);

            if (one === null) {
                return failed('unreadable', answeredStatus);
            }

            resolved.push(one);
        }

        return read(resolved);
    });
}

/** The message one resolved citation belongs to, which is as much of it as drawing a source where it stands needs. */
export interface CitedMessage {
    /** The message, as every other client route names it. */
    readonly storedEmailId: string;

    /** The configured account it was read from. */
    readonly account: string;

    /** MailFathom's own name for the folder it was read from. */
    readonly folder: string;

    /** The decoded subject, or `null` where the message carried none. */
    readonly subject: string | null;

    /** When the sender says it was sent, or `null` where the message wrote no usable date. */
    readonly sentAt: string | null;

    /** When the last receiving hop recorded it, or `null` where no header carried a usable date. */
    readonly receivedAt: string | null;
}

/** The passage one resolved citation points at, which is what a reader checks a fact against. */
export interface CitedFragment {
    /** The passage, as the citation named it. */
    readonly fragmentId: string;

    /** Its position in the message, counted from zero in reading order. */
    readonly ordinal: number;

    readonly text: string;
}

/**
 * What following one of a plan's citations produced.
 *
 * The three outcomes are three states and none of them is a failure, which is why the members below are each read
 * against the outcome rather than assumed: a resolved citation carries the message it stands in and the place inside
 * it the target named, a message whose cited place has been re-cut since carries the message alone, and a source this
 * sign-in may not read carries nothing but the target that named it.
 */
export interface ResolvedCitation {
    /** What was asked, which is what pairs a resolution with the source that named it. */
    readonly target: CitationTarget;

    readonly outcome: CitedPassageOutcome;

    /** The message the citation stands in, or `null` for a private source and for a stored copy that could not be read. */
    readonly message: CitedMessage | null;

    /** The passage the citation points at, or `null` where it points at none or that passage is gone. */
    readonly fragment: CitedFragment | null;

    /** The file the citation points at, or `null` where it points at none or that file is gone. */
    readonly file: MailAttachment | null;
}

/**
 * Follows the targets a presentation plan declared its sources under, answering an expected failure as a value.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param targets The targets to follow, at most {@link mostCitedPassages} of them, in the order they are answered for.
 * @returns One resolution per target in the order they were named, or why none arrived.
 */
export function readCitations(
    session: ClientSession,
    transport: MailFathomTransport,
    targets: readonly CitationTarget[],
): Promise<ClientResult<readonly ResolvedCitation[]>> {
    return spanned(`POST ${citedPassagesRoute}`, async () => {
        const answered = await askForResolutions(session, transport, targets);

        if (answered.outcome === 'failed') {
            return answered;
        }

        const resolved: ResolvedCitation[] = [];
        for (const [at, citation] of answered.value.entries()) {
            const target = targets[at];
            const one = target === undefined ? null : parseCitationResolution(citation, target);

            if (one === null) {
                return failed('unreadable', answeredStatus);
            }

            resolved.push(one);
        }

        return read(resolved);
    });
}

// The one status a resolution is ever read under, which is what a body this client cannot read is reported against.
const answeredStatus = 200;

/**
 * Asks the route and answers the resolutions it returned, without reading what any one of them holds.
 *
 * The bound on the batch, the refusal of an empty one, what a status means, and that the answer holds one resolution
 * per citation in the order they were named are the route's own and are stated here once. What a resolution carries is
 * what the two callers above read differently, so neither reading is here.
 */
async function askForResolutions(
    session: ClientSession,
    transport: MailFathomTransport,
    citations: readonly unknown[],
): Promise<ClientResult<readonly unknown[]>> {
    if (citations.length === 0 || citations.length > mostCitedPassages) {
        // Refused here rather than sent, because a request the route answers `400` to is one this client composed
        // wrongly: the reader would meet it as a deployment that would not answer, which is not what happened.
        return failed('unreadable', null);
    }

    const response = await send(transport, {
        method: 'POST',
        path: routeFor(session, citedPassagesRoute),
        headers: { ...headersFor(session), 'Content-Type': 'application/json' },
        body: JSON.stringify({ citations }),
        longestAnswer: longestCitationAnswer,
    });

    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    let parsed: unknown;

    try {
        parsed = JSON.parse(response.body);
    } catch {
        return failed('unreadable', response.status);
    }

    const record = asRecord(parsed);
    const resolutions = record === null ? undefined : record['citations'];

    // The answer is held against what was asked for as well as against its own shape: the route answers one resolution
    // per citation in the order the request named them, so a count that disagrees is an answer this client cannot pair
    // with what it asked about and refuses rather than draws against the wrong citation.
    return Array.isArray(resolutions) && resolutions.length === citations.length
        ? read(resolutions)
        : failed('unreadable', response.status);
}

function parsePassageResolution(value: unknown, passage: string): CitedPassage | null {
    const record = asRecord(value);
    const outcome = record === null ? null : parseOutcome(record['outcome']);

    if (record === null || outcome === null) {
        return null;
    }

    const written = record['fragment'] ?? null;
    const resolved = outcome === 'Resolved';

    // The outcome and the passage beside it are one answer rather than two, so an answer whose halves disagree is
    // refused rather than reconciled. It is the privacy half that makes this a refusal rather than a tidiness: a
    // deployment answering `PrivateSource` and attaching the words anyway would have those words drawn, because what
    // a screen shows is the passage it was handed and the outcome is what it says where there is none.
    if (resolved === (written === null)) {
        return null;
    }

    if (written === null) {
        return { passage, outcome, ordinal: null, text: null };
    }

    const fragment = parseFragment(written, passage);

    return fragment === null ? null : { passage, outcome, ordinal: fragment.ordinal, text: fragment.text };
}

/**
 * One resolution read against the target that asked for it, or `null` where the two do not hold together.
 *
 * Every refusal here is the resolver's own shape restated on this side of the boundary. A private source carries
 * nothing, because a deployment answering `PrivateSource` and attaching the mail anyway would have that mail drawn; a
 * resolved citation carries the message it stands in, because a place with no message is a place nothing can be opened
 * at; a place that is gone is reported as gone rather than as some other place in the same message; and a target that
 * pointed at no place carries none.
 */
function parseCitationResolution(value: unknown, target: CitationTarget): ResolvedCitation | null {
    const record = asRecord(value);
    const outcome = record === null ? null : parseOutcome(record['outcome']);

    if (record === null || outcome === null) {
        return null;
    }

    const written = {
        message: record['message'] ?? null,
        fragment: record['fragment'] ?? null,
        file: record['attachment'] ?? null,
    };

    if (outcome === 'PrivateSource') {
        return written.message === null && written.fragment === null && written.file === null
            ? { target, outcome, message: null, fragment: null, file: null }
            : null;
    }

    // A message that could not be read at all is the one resolution carrying none, and it is only ever unresolvable:
    // there was no reading of the message to carry, which is a different thing from a source somebody may not read.
    if (written.message === null) {
        return outcome === 'Unresolvable' && written.fragment === null && written.file === null
            ? { target, outcome, message: null, fragment: null, file: null }
            : null;
    }

    const message = parseCitedMessage(written.message);
    if (message === null) {
        return null;
    }

    // The route answers in the order it was asked, so a resolution standing in a message other than the one this
    // position asked about is an answer that would draw one fact's source under another's.
    if (message.storedEmailId !== target.email) {
        return null;
    }

    const place = parseCitedPlace(written, target, outcome);

    return place === null ? null : { target, outcome, message, fragment: place.fragment, file: place.file };
}

/**
 * The place inside the message a resolution points at, or `null` where it is not the place the target asked about.
 *
 * A resolved citation resolves to what its own kind names and to nothing else, so a passage answered for an attachment
 * target — or either answered for a target that named the message as such — is an answer put against the wrong
 * question rather than a place to draw.
 */
function parseCitedPlace(
    written: { readonly fragment: unknown; readonly file: unknown },
    target: CitationTarget,
    outcome: CitedPassageOutcome,
): { readonly fragment: CitedFragment | null; readonly file: MailAttachment | null } | null {
    if (outcome === 'Unresolvable' || target.kind === 'email') {
        // The place a target named is gone, or it named none. Either way nothing inside the message is carried, and a
        // resolution carrying one anyway is one this client cannot pair with what it asked.
        return written.fragment === null && written.file === null ? { fragment: null, file: null } : null;
    }

    if (target.kind === 'fragment') {
        const fragment = written.file !== null ? null : parseFragment(written.fragment, target.fragment);

        return fragment === null ? null : { fragment, file: null };
    }

    const file = written.fragment === null ? parseAttachment(written.file) : null;

    if (file === null) {
        return null;
    }

    return file.position === target.attachmentPosition ? { fragment: null, file } : null;
}

function parseCitedMessage(value: unknown): CitedMessage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const storedEmailId = record['storedEmailId'];
    const account = record['account'];
    const folder = record['folder'];
    const subject = record['subject'] ?? null;
    const sentAt = record['sentAt'] ?? null;
    const receivedAt = record['receivedAt'] ?? null;

    if (!isIdentity(storedEmailId) || !isIdentity(account) || !isIdentity(folder)) {
        return null;
    }

    if (!isOptionalText(subject) || !isOptionalText(sentAt) || !isOptionalText(receivedAt)) {
        return null;
    }

    return { storedEmailId, account, folder, subject, sentAt, receivedAt };
}

/** One passage, read against the identity the citation that asked for it named. */
function parseFragment(value: unknown, named: string): CitedFragment | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const fragmentId = record['fragmentId'];
    const ordinal = record['ordinal'];
    const text = record['text'];

    if (!isIdentity(fragmentId)) {
        return null;
    }

    // The route answers in the order it was asked, so a resolution naming a passage other than the one this position
    // asked about is an answer that would put one citation's words under another's.
    if (fragmentId !== named) {
        return null;
    }

    if (typeof ordinal !== 'number' || !Number.isSafeInteger(ordinal) || ordinal < 0) {
        return null;
    }

    return typeof text === 'string' && text.length <= longestPassage ? { fragmentId, ordinal, text } : null;
}

function parseOutcome(value: unknown): CitedPassageOutcome | null {
    return typeof value === 'string' && outcomes.includes(value as CitedPassageOutcome)
        ? (value as CitedPassageOutcome)
        : null;
}

function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity;
}

function isOptionalText(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && value.length <= longestText);
}
