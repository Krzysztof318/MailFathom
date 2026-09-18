// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The signed-in person's own address book, which this surface serves as two books rather than as one listing with a
// filter: what somebody wrote down and what their own mailboxes picked up from mail that arrived are different things
// to whoever is reading them, and the service publishes a route for each.
//
// Nothing here names a user. Which books are reached is resolved from the session, so a contact somebody else holds
// answers exactly as one nobody holds.
//
// **A write reports an outcome rather than refusing.** A person the books do not hold, an address another contact
// already holds, a record whose origin refuses the writer, and a promotion of somebody already asserted are each named
// in the answer, because each is something a screen reports and carries on from. So `ClientResult` here means *the
// request was answered*, and what the book did about it is the outcome inside it.

/** The route the people this user wrote down are served at, relative to the client prefix. */
export const contactsRoute = '/contacts';

/** The route the people this user's own mailboxes picked up are served at, relative to the client prefix. */
export const collectedContactsRoute = '/contacts/collected';

/** The route one contact is read, amended, and erased at, relative to the client prefix. */
export function contactRoute(contactId: string): string {
    return `${contactsRoute}/${encodeURIComponent(contactId)}`;
}

/** The route a collected contact is taken on at, relative to the client prefix. */
export function contactPromotionRoute(contactId: string): string {
    return `${contactRoute(contactId)}/promotion`;
}

/** How a contact came to be in the book, which decides who may amend it. */
export type ContactOrigin = 'Asserted' | 'Collected';

/** One person as the book holds them. */
export interface Contact {
    readonly id: string;
    readonly displayName: string;

    /** Every address they use, the preferred one first and the rest in the service's own comparison order. */
    readonly addresses: readonly string[];

    readonly preferredAddress: string;

    /** What the user wrote about them, or `null` where they wrote nothing. */
    readonly note: string | null;

    readonly origin: ContactOrigin;
    readonly recordedAt: string;
    readonly amendedAt: string;
}

/** One bounded page of one book, and where the walk continues. */
export interface ContactPage {
    readonly contacts: readonly Contact[];

    /**
     * The cursor the following page is asked with, or `null` at the end of the book.
     *
     * The absent cursor is the end of the walk rather than a page that happened to be short, so a caller stops when
     * this stops instead of comparing the count against the size it asked for.
     */
    readonly nextCursor: string | null;
}

/** How a write to the book ended, in the vocabulary the surface publishes. */
export type ContactWriteOutcome =
    'Written' | 'NotFound' | 'AddressHeldByAnotherContact' | 'OriginRefusesWriter' | 'AlreadyAsserted';

/** What one write to the book produced. */
export interface ContactWrite {
    readonly outcome: ContactWriteOutcome;

    /** The record the caller itself stated, present exactly where that write was performed. */
    readonly contact: Contact | null;

    /** The contact already holding an address the write claimed, present exactly where that is what refused it. */
    readonly addressHolder: string | null;
}

/** The record a caller states for a contact, whether it is being written for the first time or amended. */
export interface ContactRecord {
    readonly displayName: string;

    /** Every address the person uses; two spellings of one address count once. */
    readonly addresses: readonly string[];

    /** The address to use by default, which the service requires to be one of `addresses`. */
    readonly preferredAddress: string;

    /** What the user wrote about the person, or `null` to hold no note. */
    readonly note: string | null;
}

/** What erasing one contact removed, which carries no name, address, or note by design. */
export interface ContactErasure {
    readonly contact: string;
    readonly wasHeld: boolean;
    readonly addressesErased: number;
}

/**
 * The most contacts one page may name, which is the deployment's own bound rather than a preference.
 *
 * A request asking for more than this is refused rather than quietly served this many, so a caller states a window it
 * can render instead of discovering the bound from a refusal.
 */
export const mostContactsPerPage = 200;

/** How many a screen asks for where it states no window of its own, which is the size the service serves by default. */
export const contactsPerPage = 50;

// The most addresses one contact may carry before the answer is refused unread. Far above anything a person records
// and far below anything worth buffering: what the bound guards against is an answer that was never a contact.
const mostAddressesPerContact = 64;

// A contact is a name, a handful of addresses, a note, and four short fields. Generous against that arithmetic over a
// full page and well under the transport's own backstop.
const longestContactPage = 512 * 1024;

// One contact and one write answer are each a single record of the same shape.
const longestContactAnswer = 32 * 1024;

const contactOrigins: readonly ContactOrigin[] = ['Asserted', 'Collected'];

const contactWriteOutcomes: readonly ContactWriteOutcome[] = [
    'Written',
    'NotFound',
    'AddressHeldByAnotherContact',
    'OriginRefusesWriter',
    'AlreadyAsserted',
];

/** Reads one page of the people this user wrote down. */
export function readOwnContacts(
    session: ClientSession,
    transport: MailFathomTransport,
    page: { readonly pageSize?: number; readonly cursor?: string | null } = {},
): Promise<ClientResult<ContactPage>> {
    return readBook(session, transport, contactsRoute, page);
}

/** Reads one page of the people this user's own mailboxes picked up from mail that arrived. */
export function readCollectedContacts(
    session: ClientSession,
    transport: MailFathomTransport,
    page: { readonly pageSize?: number; readonly cursor?: string | null } = {},
): Promise<ClientResult<ContactPage>> {
    return readBook(session, transport, collectedContactsRoute, page);
}

/**
 * Reads one person out of whichever of this user's books holds them.
 *
 * A `404` here is `missing` rather than `unavailable`, which is the reading `failureReasonForStatus` leaves to a route
 * that names one thing: a contact erased while somebody had them open is let go of, where a deployment that is down is
 * retried.
 */
export function readContact(
    session: ClientSession,
    transport: MailFathomTransport,
    contactId: string,
): Promise<ClientResult<Contact>> {
    return spanned(`GET ${contactsRoute}/{contactId}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, contactRoute(contactId)),
            headers: headersFor(session),
            longestAnswer: longestContactAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const contact = parseContact(bodyOf(response));

        return contact === null ? failed('unreadable', response.status) : read(contact);
    });
}

/** Records somebody this user's own book does not yet hold, as a person they asserted. */
export function recordContact(
    session: ClientSession,
    transport: MailFathomTransport,
    record: ContactRecord,
): Promise<ClientResult<ContactWrite>> {
    return spanned(`POST ${contactsRoute}`, async () =>
        writeOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, contactsRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(record),
                longestAnswer: longestContactAnswer,
            }),
        ),
    );
}

/** Takes on a collected record, so it becomes one this user asserted and may amend. */
export function promoteContact(
    session: ClientSession,
    transport: MailFathomTransport,
    contactId: string,
): Promise<ClientResult<ContactWrite>> {
    return spanned(`POST ${contactsRoute}/{contactId}/promotion`, async () =>
        writeOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, contactPromotionRoute(contactId)),
                headers: headersFor(session),
                longestAnswer: longestContactAnswer,
            }),
        ),
    );
}

/** Erases one person and everything the book derived from them, whichever book holds them. */
export function eraseContact(
    session: ClientSession,
    transport: MailFathomTransport,
    contactId: string,
): Promise<ClientResult<ContactErasure>> {
    return spanned(`DELETE ${contactsRoute}/{contactId}`, async () => {
        const response = await send(transport, {
            method: 'DELETE',
            path: routeFor(session, contactRoute(contactId)),
            headers: headersFor(session),
            longestAnswer: longestContactAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const erasure = parseErasure(bodyOf(response));

        return erasure === null ? failed('unreadable', response.status) : read(erasure);
    });
}

function readBook(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    page: { readonly pageSize?: number; readonly cursor?: string | null },
): Promise<ClientResult<ContactPage>> {
    // The window the screen can render rather than whatever the service would serve, and never more than the bound the
    // route enforces — a request past it is refused, which would cost a reader a page for a number they never chose.
    const pageSize = Math.min(page.pageSize ?? contactsPerPage, mostContactsPerPage);

    return spanned(`GET ${route}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, route)}?${pageArguments(pageSize, page.cursor ?? null)}`,
            headers: headersFor(session),
            longestAnswer: longestContactPage,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const book = parsePage(bodyOf(response), pageSize);

        return book === null ? failed('unreadable', response.status) : read(book);
    });
}

// Written out rather than composed with `URLSearchParams`, which is a browser API and therefore one this package
// declares nothing of: the wire half of the client knows a route and a status code and nothing about a document.
function pageArguments(pageSize: number, cursor: string | null): string {
    const asked = [`pageSize=${pageSize.toFixed(0)}`];

    if (cursor !== null && cursor !== '') {
        asked.push(`cursor=${encodeURIComponent(cursor)}`);
    }

    return asked.join('&');
}

function bodyOf(response: ClientResponse): unknown {
    try {
        return JSON.parse(response.body);
    } catch {
        return null;
    }
}

function writeOf(response: ClientResponse | null): ClientResult<ContactWrite> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const write = parseWrite(bodyOf(response));

    return write === null ? failed('unreadable', response.status) : read(write);
}

function parsePage(value: unknown, asked: number): ContactPage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const entries = record['contacts'];
    const nextCursor = record['nextCursor'] ?? null;

    if (!Array.isArray(entries) || entries.length > asked) {
        return null;
    }

    if (nextCursor !== null && typeof nextCursor !== 'string') {
        return null;
    }

    const contacts: Contact[] = [];
    for (const entry of entries) {
        const contact = parseContact(entry);
        if (contact === null) {
            return null;
        }

        contacts.push(contact);
    }

    return { contacts, nextCursor };
}

function parseWrite(value: unknown): ContactWrite | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const outcome = record['outcome'];
    const stated = record['contact'] ?? null;
    const addressHolder = record['addressHolder'] ?? null;

    if (!isWriteOutcome(outcome)) {
        return null;
    }

    if (addressHolder !== null && typeof addressHolder !== 'string') {
        return null;
    }

    if (stated === null) {
        return { outcome, contact: null, addressHolder };
    }

    const contact = parseContact(stated);

    return contact === null ? null : { outcome, contact, addressHolder };
}

function parseErasure(value: unknown): ContactErasure | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const contact = record['contact'];
    const wasHeld = record['wasHeld'];
    const addressesErased = record['addressesErased'];

    if (typeof contact !== 'string' || typeof wasHeld !== 'boolean' || typeof addressesErased !== 'number') {
        return null;
    }

    return { contact, wasHeld, addressesErased };
}

/** Reads one contact off a response body, or answers `null` where any field of it is missing or of the wrong shape. */
export function parseContact(value: unknown): Contact | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const displayName = record['displayName'];
    const addresses = record['addresses'];
    const preferredAddress = record['preferredAddress'];
    const note = record['note'] ?? null;
    const origin = record['origin'];
    const recordedAt = record['recordedAt'];
    const amendedAt = record['amendedAt'];

    if (typeof id !== 'string' || typeof displayName !== 'string' || typeof preferredAddress !== 'string') {
        return null;
    }

    if (typeof recordedAt !== 'string' || typeof amendedAt !== 'string' || !isContactOrigin(origin)) {
        return null;
    }

    if (note !== null && typeof note !== 'string') {
        return null;
    }

    if (!Array.isArray(addresses) || addresses.length === 0 || addresses.length > mostAddressesPerContact) {
        return null;
    }

    for (const address of addresses) {
        if (typeof address !== 'string') {
            return null;
        }
    }

    return {
        id,
        displayName,
        addresses: addresses as readonly string[],
        preferredAddress,
        note,
        origin,
        recordedAt,
        amendedAt,
    };
}

/** Whether the value is one of the two origins this surface publishes. */
export function isContactOrigin(value: unknown): value is ContactOrigin {
    return typeof value === 'string' && contactOrigins.includes(value as ContactOrigin);
}

function isWriteOutcome(value: unknown): value is ContactWriteOutcome {
    return typeof value === 'string' && contactWriteOutcomes.includes(value as ContactWriteOutcome);
}
