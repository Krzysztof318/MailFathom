// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// The draft somebody is about to write, composed by the deployment out of the conversation it answers or out of what
// they typed. What comes back is a proposal rather than a message: nothing is sent, nothing is filed, and the text
// arrives in a composer its author edits, discards, or saves through the routes that already do those things.
//
// **Two routes on one address, because a composer has two questions and only one of them costs anything.** The read
// says whether this deployment drafts at all, which is what decides whether the affordance is drawn before anybody
// presses it — a control promising a draft over a deployment that writes none fails a person at the one moment they
// trusted it. The write drafts one and costs a provider call.
//
// **Three answers rather than a value and a failure**, because a composer does something different with each. A draft
// is put on the screen. A deployment that drafts none, and one whose provider could not be reached, are the composer
// as it was and are not a failure anybody can act on — which is why the service answers both `200` and this module
// reports them as one outcome rather than inventing a difference. An allowance the deployment has already spent is the
// one that travels, because somebody pressed a button the operator has paid the last of the budget for and being told
// nothing is what would send them pressing it again.
//
// Everything here is derived from somebody's own mail and is personal data of the same standing, so none of it is
// logged, kept, or put in a span attribute; the span names the route and carries nothing of the draft.

/** The route a reply is drafted at, relative to the client prefix. */
export const mailReplyDraftingRoute = '/replies/drafting';

/** One message a claim rests on, spelled as every citation on this surface is spelled. */
export interface MailReplyDraftSource {
    readonly kind: 'email';
    readonly email: string;
}

/** One thing the drafted message asserts, and what the correspondence does or does not say about it. */
export interface MailReplyDraftClaim {
    /** The assertion, as one sentence of the draft. */
    readonly text: string;

    /**
     * Whether the correspondence backs it.
     *
     * Read rather than derived from the sources being empty: what it means is a promise the deployment makes about the
     * draft, and a client deciding it from an array's length would be re-deciding, per client, what an unsupported
     * claim is.
     */
    readonly supported: boolean;

    /** The messages backing it, best first, and empty for a claim the correspondence does not carry. */
    readonly sources: readonly MailReplyDraftSource[];
}

/** One person the draft proposes to reach, every one of whom the conversation itself names. */
export interface MailReplyDraftRecipient {
    readonly address: string;
    readonly displayName: string | null;
}

/** A draft the deployment wrote, as a composer puts it on the screen. */
export interface MailReplyDraft {
    readonly outcome: 'drafted';

    /** The draft as plain text, which is what the composer's body is set from. */
    readonly body: string;

    /** What the draft asserts, in the order it was written. */
    readonly claims: readonly MailReplyDraftClaim[];

    /** Who the draft proposes to reach, shown as proposals and added to nothing on anybody's behalf. */
    readonly proposedRecipients: readonly MailReplyDraftRecipient[];
}

/** What a drafting answered with, which is a draft or one of the two reasons there is none. */
export type MailReplyDrafting =
    MailReplyDraft | { readonly outcome: 'notDrafted' } | { readonly outcome: 'allowanceSpent' };

/** The answer a deployment that writes no draft gives, which leaves the composer as it was. */
export const nothingDrafted: MailReplyDrafting = { outcome: 'notDrafted' };

/** What one drafting asks for: the message being answered, if any, and what its author wants said. */
export interface MailReplyDraftRequest {
    /** The message being answered, or `null` where the message being written answers none. */
    readonly answeredEmailId: string | null;

    /** The part of the correspondence being answered, or `null` to answer it as a whole. */
    readonly selection: string | null;

    /** What the draft should say, or `null` where the person asked for nothing in particular. */
    readonly instruction: string | null;
}

/**
 * The greatest instruction the service reads.
 *
 * The client's own copy of the service's bound, so a composer shortens what it sends rather than putting a request
 * together that the deployment will refuse with a sentence nobody asked for.
 */
export const longestDraftInstruction = 1_000;

/** The greatest selection the service reads, on the same terms as the instruction above. */
export const longestDraftSelection = 4_000;

// A draft is a message and a handful of short claims, so this is generous against what the route answers and exists
// for the answer that is not a draft at all.
const longestDraftAnswer = 128 * 1024;

// What one answer may carry before the draft is refused unread. The service bounds each of these itself; these are the
// client's own copy of the bound, checked while the collections are walked rather than after.
const longestIdentity = 256;
const longestBody = 32_000;
const longestClaimText = 4_096;
const longestAddress = 512;
const mostClaims = 32;
const mostSourcesPerClaim = 16;
const mostProposedRecipients = 32;

/**
 * Reads whether this deployment drafts a message at all, which is what decides whether the affordance is offered.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @returns Whether a draft is written, or why the answer never arrived.
 */
export function draftsMailReplies(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<boolean>> {
    return spanned(`GET ${mailReplyDraftingRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailReplyDraftingRoute),
            headers: headersFor(session),
            longestAnswer: longestDraftAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const record = parsed(response.body);
        const drafts = record === null ? null : record['draftsReplies'];

        return typeof drafts === 'boolean' ? read(drafts) : failed('unreadable', response.status);
    });
}

/**
 * Drafts one message out of the conversation it answers, or out of what its author asked for.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param request The message being answered, if any, and the two texts beside it.
 * @returns The draft, the two shapes of having none, or why the answer never arrived.
 */
export function draftMailReply(
    session: ClientSession,
    transport: MailFathomTransport,
    request: MailReplyDraftRequest,
): Promise<ClientResult<MailReplyDrafting>> {
    return spanned(`POST ${mailReplyDraftingRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, mailReplyDraftingRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                answeredEmailId: request.answeredEmailId,
                selection: shortened(request.selection, longestDraftSelection),
                instruction: shortened(request.instruction, longestDraftInstruction),
            }),
            longestAnswer: longestDraftAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        // The one non-success status this route answers that a composer draws rather than reports as something going
        // wrong, because the operator has spent the allowance and the person pressing the control has to be told so.
        if (response.status === 429) {
            return read({ outcome: 'allowanceSpent' });
        }

        // The message the composer was opened over is gone. A draft of it is not something to retry and not something
        // to sign in again for, so it is the composer as it was, which is the answer a deployment drafting none gives.
        if (response.status === 404) {
            return read(nothingDrafted);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const draft = parseDrafting(response.body);

        return draft === null ? failed('unreadable', response.status) : read(draft);
    });
}

/** Cuts a typed text to what the service reads, so a composer sends a request the deployment will answer. */
function shortened(typed: string | null, longest: number): string | null {
    const written = typed?.trim() ?? '';

    return written.length === 0 ? null : written.slice(0, longest);
}

function parsed(body: string): Record<string, unknown> | null {
    try {
        return asRecord(JSON.parse(body));
    } catch {
        return null;
    }
}

function parseDrafting(body: string): MailReplyDrafting | null {
    const record = parsed(body);
    if (record === null) {
        return null;
    }

    if (record['drafted'] !== true) {
        return record['drafted'] === false ? nothingDrafted : null;
    }

    const draftBody = record['body'];
    if (typeof draftBody !== 'string' || draftBody.length === 0 || draftBody.length > longestBody) {
        return null;
    }

    const claims = parseClaims(record['claims']);
    const proposedRecipients = parseRecipients(record['proposedRecipients']);

    return claims === null || proposedRecipients === null
        ? null
        : { outcome: 'drafted', body: draftBody, claims, proposedRecipients };
}

function parseClaims(value: unknown): readonly MailReplyDraftClaim[] | null {
    if (!Array.isArray(value) || value.length > mostClaims) {
        return null;
    }

    const claims: MailReplyDraftClaim[] = [];
    for (const written of value) {
        const record = asRecord(written);
        const text = record?.['text'];
        const supported = record?.['supported'];

        if (typeof text !== 'string' || text.length === 0 || text.length > longestClaimText) {
            return null;
        }

        if (typeof supported !== 'boolean') {
            return null;
        }

        const sources = parseSources(record?.['sources']);
        if (sources === null) {
            return null;
        }

        claims.push({ text, supported, sources });
    }

    return claims;
}

function parseSources(value: unknown): readonly MailReplyDraftSource[] | null {
    if (!Array.isArray(value) || value.length > mostSourcesPerClaim) {
        return null;
    }

    const sources: MailReplyDraftSource[] = [];
    for (const written of value) {
        const record = asRecord(written);
        const email = record?.['email'];

        if (record?.['kind'] !== 'email' || typeof email !== 'string' || email.length === 0) {
            return null;
        }

        if (email.length > longestIdentity) {
            return null;
        }

        sources.push({ kind: 'email', email });
    }

    return sources;
}

function parseRecipients(value: unknown): readonly MailReplyDraftRecipient[] | null {
    if (!Array.isArray(value) || value.length > mostProposedRecipients) {
        return null;
    }

    const recipients: MailReplyDraftRecipient[] = [];
    for (const written of value) {
        const record = asRecord(written);
        const address = record?.['address'];
        const displayName = record?.['displayName'] ?? null;

        if (typeof address !== 'string' || address.length === 0 || address.length > longestAddress) {
            return null;
        }

        if (displayName !== null && (typeof displayName !== 'string' || displayName.length > longestAddress)) {
            return null;
        }

        recipients.push({ address, displayName });
    }

    return recipients;
}
