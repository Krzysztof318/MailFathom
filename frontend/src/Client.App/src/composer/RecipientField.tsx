// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import { longestContactSearch, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { looksLikeAnAddress, mostRecipientsInOneHeader } from './composition';
import { searchContacts, suggestionsFor, type ContactLookup, type RecipientSuggestion } from './recipientSuggestions';

// One header of a message being written, as the design project draws it: the header's name, a chip per address with a
// way to take each one back off, and a field to write the next one in.
//
// **The name stands in the composer's own label column.** The design draws the whole header as one grid — a fixed
// column of names and a column of what is written under each — so this row and the subject row below it state the same
// width rather than each measuring its own name, and the chips and the subject share a left edge.
//
// **What the field offers is a list this component draws**, as the combobox pattern the platform's accessibility
// guidance describes: the field keeps the keyboard, the arrows move through the list, Enter takes the entry in force,
// and Escape puts the list away. A native `datalist` cannot do what the list is for — it matches the text of its
// options alone, so nobody is found by their name, and it draws no second line to say which address a name stands for.

/** How long the field waits after a keystroke before searching the book, so a word typed is one search rather than five. */
const searchDelay = 200;

export function RecipientField({
    label,
    placeholder,
    addresses,
    participants,
    session,
    transport,
    onChanged,
    trailing,
}: {
    /** What this header is called, which is what the field is named by and what a chip's removal names. */
    readonly label: string;

    /** What the empty field says, which the design words differently for each header. */
    readonly placeholder: string;

    readonly addresses: readonly string[];

    /** The people in the conversation being answered, which are offered before anybody in the book. */
    readonly participants: readonly RecipientSuggestion[];

    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    readonly onChanged: (addresses: readonly string[]) => void;

    /** What stands at the end of the row, which the design puts the reveal for the copy headers in. */
    readonly trailing?: ReactNode;
}) {
    const { translate } = useLocalization();
    const [written, setWritten] = useState('');
    const [refused, setRefused] = useState<string | null>(null);
    const [focused, setFocused] = useState(false);
    const [dismissed, setDismissed] = useState(false);
    const [active, setActive] = useState<number | null>(null);
    // The term an answer was for travels with it: its people are narrowed to what is typed now, while what it says about
    // the book is only true of the term it was asked for.
    const [lookup, setLookup] = useState<{ readonly term: string; readonly answer: ContactLookup } | null>(null);
    const fieldId = useId();
    const listId = useId();

    const term = written.trim();
    const searchable = term !== '' && term.length <= longestContactSearch;

    // Searching the book is a request going out, which is what an effect is for. The answer is discarded where the term
    // moved on before it arrived, and the wait before asking is what keeps a word typed from being a search per letter.
    useEffect(() => {
        if (!searchable) {
            return;
        }

        let listening = true;

        const waiting = setTimeout(() => {
            void searchContacts(session, transport, term).then((answer) => {
                if (listening) {
                    setLookup({ term, answer });
                }
            });
        }, searchDelay);

        return () => {
            listening = false;
            clearTimeout(waiting);
        };
    }, [searchable, term, session, transport]);

    const fromTheBook = lookup?.answer.suggestions ?? [];
    const offered = searchable ? suggestionsFor(term, participants, fromTheBook, addresses) : [];
    const unsearchable = searchable && lookup?.term === term && lookup.answer.incomplete;
    const open = focused && !dismissed && (offered.length > 0 || unsearchable);
    // The popup can hold the note alone, and the combobox names only a listbox that is drawn.
    const listed = open && offered.length > 0;
    const inForce = open && active !== null ? offered[active] : undefined;

    // The field's own text is committed on Enter, on a comma, and on leaving the field, because all three are ways
    // somebody signals they have finished writing one address. What it refuses says why rather than doing nothing.
    function commit(text: string): void {
        const address = text.trim().replace(/,$/u, '');

        if (address === '') {
            setRefused(null);
            setWritten('');

            return;
        }

        if (!looksLikeAnAddress(address)) {
            setRefused(translate('compose.notAnAddress'));

            return;
        }

        if (addresses.includes(address)) {
            setRefused(translate('compose.alreadyAddressed', { address }));

            return;
        }

        if (addresses.length >= mostRecipientsInOneHeader) {
            setRefused(translate('compose.tooManyAddresses', { count: mostRecipientsInOneHeader.toFixed(0) }));

            return;
        }

        setWritten('');
        setRefused(null);
        setActive(null);
        setLookup(null);
        onChanged([...addresses, address]);
    }

    // The arrows walk the list and wrap at either end, Enter takes what they rest on, and Escape puts the list away
    // without leaving the field, until the next keystroke asks for it again.
    function moveThroughTheList(event: KeyboardEvent<HTMLInputElement>): void {
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && (offered.length > 0 || unsearchable)) {
            event.preventDefault();
            setDismissed(false);

            if (offered.length === 0) {
                return;
            }

            const step = event.key === 'ArrowDown' ? 1 : -1;
            const from = active ?? (step === 1 ? -1 : 0);

            setActive((from + step + offered.length) % offered.length);

            return;
        }

        if (event.key === 'Escape' && open) {
            event.preventDefault();
            setDismissed(true);
            setActive(null);

            return;
        }

        if (event.key === 'Enter') {
            event.preventDefault();
            commit(inForce?.address ?? written);
        }
    }

    return (
        <div className="relative flex flex-wrap items-center gap-2.5 border-b border-line-soft px-3.75 py-2.25 focus-within:ring-2 focus-within:ring-accent focus-within:ring-inset">
            <label htmlFor={fieldId} className="w-22 shrink-0 text-sm text-muted">
                {label}
            </label>

            {addresses.map((address) => (
                <span
                    key={address}
                    className="flex items-center gap-1.75 rounded-4xl border border-line bg-rail px-2.5 py-0.75 text-base"
                >
                    {address}
                    <button
                        type="button"
                        aria-label={translate('compose.removeRecipient', { address, header: label })}
                        className="flex items-center rounded-xs text-faint transition hover:text-text"
                        onClick={() => {
                            onChanged(addresses.filter((kept) => kept !== address));
                        }}
                    >
                        <Icon name="close" className="size-3.5" />
                    </button>
                </span>
            ))}

            <input
                id={fieldId}
                role="combobox"
                aria-autocomplete="list"
                aria-expanded={listed}
                aria-controls={listed ? listId : undefined}
                aria-activedescendant={inForce === undefined ? undefined : `${listId}-${active?.toFixed(0) ?? ''}`}
                value={written}
                inputMode="email"
                autoComplete="off"
                placeholder={placeholder}
                className="min-w-40 flex-1 border-none bg-transparent text-base text-text outline-none placeholder:text-faint"
                onChange={(event) => {
                    setRefused(null);
                    setDismissed(false);
                    setActive(null);
                    setWritten(event.target.value);

                    // A comma is how one address is written after another, so it commits what stands before it rather
                    // than becoming part of an address nothing would accept.
                    if (event.target.value.endsWith(',')) {
                        commit(event.target.value);
                    }
                }}
                onKeyDown={moveThroughTheList}
                onFocus={() => {
                    setFocused(true);
                }}
                onBlur={() => {
                    setFocused(false);
                    setActive(null);
                    commit(written);
                }}
            />

            {trailing}

            {open ? (
                <div className="absolute inset-x-3.75 top-full z-20 mt-1 overflow-hidden rounded-xl border border-line bg-panel py-1 shadow-overlay">
                    {listed ? (
                        <ul
                            id={listId}
                            role="listbox"
                            aria-label={translate('compose.suggestedRecipients', { header: label })}
                        >
                            {offered.map((suggestion, index) => (
                                <SuggestedRecipient
                                    key={suggestion.address}
                                    id={`${listId}-${index.toFixed(0)}`}
                                    suggestion={suggestion}
                                    inForce={index === active}
                                    onChosen={() => {
                                        commit(suggestion.address);
                                    }}
                                />
                            ))}
                        </ul>
                    ) : null}

                    {unsearchable ? (
                        <p aria-live="polite" className="px-3 py-1.75 text-sm text-muted">
                            {translate('compose.contactsUnsearchable')}
                        </p>
                    ) : null}
                </div>
            ) : null}

            {refused === null ? null : (
                <p role="alert" className="basis-full text-sm text-warning-text">
                    {refused}
                </p>
            )}
        </div>
    );
}

// One entry of the list: who it is, and the address choosing them writes in. A press is kept from taking the focus
// out of the field, which is what would otherwise commit the half-typed text before the entry was chosen.
function SuggestedRecipient({
    id,
    suggestion,
    inForce,
    onChosen,
}: {
    readonly id: string;
    readonly suggestion: RecipientSuggestion;
    readonly inForce: boolean;
    readonly onChosen: () => void;
}) {
    return (
        <li
            id={id}
            role="option"
            aria-selected={inForce}
            className={`flex cursor-pointer flex-col px-3 py-1.75 ${inForce ? 'bg-hover' : 'hover:bg-hover'}`}
            onMouseDown={(event) => {
                event.preventDefault();
            }}
            onClick={onChosen}
        >
            {suggestion.name === null ? null : <span className="truncate text-base text-text">{suggestion.name}</span>}
            <span className={`truncate ${suggestion.name === null ? 'text-base text-text' : 'text-sm text-muted'}`}>
                {suggestion.address}
            </span>
        </li>
    );
}
