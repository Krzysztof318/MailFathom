// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    readCollectedContacts,
    readOwnContacts,
    type ClientSession,
    type Contact,
    type MailFathomTransport,
} from '@mailfathom/client-backend';

// Who a recipient field offers as somebody types into it: the people in the conversation being answered first, then
// the people in the address book — both halves of it, what the person wrote down and what their mailboxes picked up —
// narrowed by a name or an address. The book is searched by the deployment, so what is matched there is its own rule;
// what is matched here is only the narrowing of an answer already on the screen, which is what keeps the list moving
// with every keystroke rather than with every answer.

/** One person a recipient field offers: the address choosing them writes in, and the name they go by where known. */
export interface RecipientSuggestion {
    readonly address: string;
    readonly name: string | null;
}

/**
 * What searching the address book answered: the people it found, and whether a half of the book the person holds went
 * unread — which a list of the other half's people does not say on its own.
 */
export interface ContactLookup {
    readonly suggestions: readonly RecipientSuggestion[];
    readonly incomplete: boolean;
}

/** The most a field lists at once, which is a list read at a glance rather than a book to page through. */
export const mostSuggestions = 8;

// How many each book is asked for. Two books fill the list between them, and the participants of a conversation come
// first, so neither book needs more than its share of what the list shows.
const suggestionsPerBook = 5;

/**
 * Searches both of the address book's halves for a term, as suggestions in the book's own order.
 *
 * A person whose grants do not reach the book is offered nobody rather than told it failed: they have no book to search,
 * and a sentence saying so under every keystroke would be a refusal nobody asked about. Any other failure of either half
 * is said, because a list that leaves out a book that went unread reads as a book with nobody else in it.
 */
export async function searchContacts(
    session: ClientSession,
    transport: MailFathomTransport,
    term: string,
): Promise<ContactLookup> {
    const answers = await Promise.all(
        [readOwnContacts, readCollectedContacts].map((read) =>
            read(session, transport, { search: term, pageSize: suggestionsPerBook }),
        ),
    );

    return {
        incomplete: answers.some((answer) => answer.outcome === 'failed' && answer.failure.reason !== 'unauthorized'),
        suggestions: answers.flatMap((answer) =>
            answer.outcome === 'read' ? answer.value.contacts.flatMap((contact) => offeredAt(contact, term)) : [],
        ),
    };
}

/**
 * What a field lists for a term: the conversation's people who match it, then the book's, each address once and none
 * the header already holds, up to what a list shows.
 *
 * The book's answer is narrowed here as well as by the deployment, because it may be the answer to the term before the
 * last keystroke — and a person who typed one more letter should not be offered somebody that letter rules out while
 * the next answer is on its way.
 */
export function suggestionsFor(
    term: string,
    participants: readonly RecipientSuggestion[],
    fromTheBook: readonly RecipientSuggestion[],
    addressed: readonly string[],
): readonly RecipientSuggestion[] {
    const written = new Set(addressed.map(addressKey));
    const candidates = [...participants, ...fromTheBook].filter((suggestion) => matches(suggestion, term));

    return candidates
        .filter(
            (suggestion, index) =>
                !written.has(addressKey(suggestion.address)) &&
                candidates.findIndex((earlier) => addressKey(earlier.address) === addressKey(suggestion.address)) ===
                    index,
        )
        .slice(0, mostSuggestions);
}

// Two spellings of one address are one person to offer, which is how the book itself compares them.
function addressKey(address: string): string {
    return address.toLowerCase();
}

// One contact as the list offers them. Somebody found by one of their addresses is offered at that address, which is
// the one the person was typing; somebody found by their name is offered at the address they prefer.
function offeredAt(contact: Contact, term: string): readonly RecipientSuggestion[] {
    const typed = contact.addresses.filter((address) => contains(address, term));
    const addresses = typed.length > 0 ? typed : [contact.preferredAddress];

    return addresses.map((address) => ({ address, name: contact.displayName }));
}

function matches(suggestion: RecipientSuggestion, term: string): boolean {
    return contains(suggestion.address, term) || (suggestion.name !== null && contains(suggestion.name, term));
}

function contains(text: string, term: string): boolean {
    return text.toLocaleLowerCase().includes(term.trim().toLocaleLowerCase());
}
