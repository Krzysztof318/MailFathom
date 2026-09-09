// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// Following a mark's evidence back to the words it rests on. The deployment publishes evidence as passage identifiers
// rather than as text — a list page carrying the passages themselves would be publishing a body it had no reason to —
// so a screen that wants to show somebody what a reading was drawn from asks for it, which is this.
//
// It asks the citation route, and only for the one kind a mark's evidence names: a passage of a message. The route
// follows two other kinds as well, and neither is composed here, because an operation this package publishes with no
// caller is a wire contract nobody is holding to. The route's own request document is flat across all three kinds, so
// the second caller adds its members rather than a second module.
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
 * A request naming more is refused with a `400`, so the client holds the same number and asks within it. A mark rests
 * on at most four passages and a message carries at most three marks, so one message's whole evidence is within it and
 * no screen here has to page.
 */
export const mostCitedPassages = 10;

// A resolved passage is one chunk of a message's text, which chunking already cut to a fixed length, and ten of them
// with their offsets and identities is what an answer holds. Well above that and far below the transport's backstop.
const longestCitationAnswer = 128 * 1024;

const longestIdentity = 256;
const longestPassage = 8_192;

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
        if (passages.length === 0 || passages.length > mostCitedPassages) {
            // Refused here rather than sent, because a request the route answers `400` to is one this client composed
            // wrongly: the reader would meet it as a deployment that would not answer, which is not what happened.
            return failed('unreadable', null);
        }

        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, citedPassagesRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                citations: passages.map((passage) => ({ kind: 'fragment', email: storedEmailId, fragment: passage })),
            }),
            longestAnswer: longestCitationAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const resolved = parseResolutions(response.body, passages);

        return resolved === null ? failed('unreadable', response.status) : read(resolved);
    });
}

// The answer is held against what was asked for as well as against its own shape: the route answers one resolution per
// citation in the order the request named them, so a count that disagrees is an answer this client cannot pair with
// the evidence it asked about and refuses rather than draws against the wrong mark.
function parseResolutions(body: string, asked: readonly string[]): readonly CitedPassage[] | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const citations = record['citations'];
    if (!Array.isArray(citations) || citations.length !== asked.length) {
        return null;
    }

    const resolved: CitedPassage[] = [];
    for (const [at, citation] of citations.entries()) {
        const passage = asked[at];
        if (passage === undefined) {
            return null;
        }

        const one = parseResolution(citation, passage);
        if (one === null) {
            return null;
        }

        resolved.push(one);
    }

    return resolved;
}

function parseResolution(value: unknown, passage: string): CitedPassage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const outcome = record['outcome'];
    if (typeof outcome !== 'string' || !outcomes.includes(outcome as CitedPassageOutcome)) {
        return null;
    }

    const fragment = record['fragment'] ?? null;
    if (fragment === null) {
        return { passage, outcome: outcome as CitedPassageOutcome, ordinal: null, text: null };
    }

    const cited = asRecord(fragment);
    if (cited === null) {
        return null;
    }

    const fragmentId = cited['fragmentId'];
    const ordinal = cited['ordinal'];
    const text = cited['text'];

    if (typeof fragmentId !== 'string' || fragmentId.length === 0 || fragmentId.length > longestIdentity) {
        return null;
    }

    // The route answers in the order it was asked, so a resolution naming a passage other than the one this position
    // asked about is an answer that would put one mark's words under another's.
    if (fragmentId !== passage) {
        return null;
    }

    if (typeof ordinal !== 'number' || !Number.isSafeInteger(ordinal) || ordinal < 0) {
        return null;
    }

    if (typeof text !== 'string' || text.length > longestPassage) {
        return null;
    }

    return { passage, outcome: outcome as CitedPassageOutcome, ordinal, text };
}
