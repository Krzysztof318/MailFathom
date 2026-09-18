// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import type { ClientFailureReason, Contact } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { onlySelected, withToggled } from '../contextMenu/rowSelection';
import { ChoiceSegment } from '../controls/ChoiceSegment';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { estimatedRowHeight, offsetOfRow, windowOf } from '../messageRows/rowWindow';
import { ContactRow } from './ContactRow';
import { ContactRowMenu } from './ContactRowMenu';
import type { ContactBookInForce, ContactBookName } from './useContactBook';

// The column the address book is read down: the two books as the design project draws them, how many people the one in
// front holds, and the rows themselves.
//
// **The two books are a choice rather than a filter**, which is what the design draws and what the surface publishes —
// so they are radio segments in a pill, announced as one set of choices, rather than two buttons whose relationship a
// reader has to work out.
//
// **The rows are windowed**, over the token height a contact row is drawn at, for the reason the message list is: a
// collected book holds everybody who has ever written, and a list that keeps every row in the document grows with it.
// The arithmetic is `messageRows/rowWindow.ts`, which the search results read as well.
//
// **A page is asked for from the scroll that reaches the end of what is read**, which is the design project's own
// scrolling rather than a control at the foot. The wait for it is drawn under the rows instead of replacing them,
// because a reader who has scrolled to the end of what is read has not left what they were reading.

// How many people the book in front holds, in the forms a language has for the noun. Selected rather than spelled,
// because Polish needs three forms and English hides that it needs two.
const bookCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'people.count.other',
    one: 'people.count.one',
    two: 'people.count.other',
    few: 'people.count.few',
    many: 'people.count.many',
    other: 'people.count.other',
};

const bookNames: Readonly<Record<ContactBookName, MessageKey>> = {
    own: 'people.own',
    collected: 'people.collected',
};

const bookHints: Readonly<Record<ContactBookName, MessageKey>> = {
    own: 'people.ownHint',
    collected: 'people.collectedHint',
};

const bookEmpty: Readonly<Record<ContactBookName, MessageKey>> = {
    own: 'people.ownEmpty',
    collected: 'people.collectedEmpty',
};

// Why the book did not answer, each said as what it is with a next step rather than as a status code. A book the
// deployment no longer holds is not one of the five this route can produce, so it is worded as the deployment not
// having answered — which is what a reader would do about it either way.
const bookFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'people.failedUnauthenticated',
    unauthorized: 'people.failedUnauthorized',
    unavailable: 'people.failedUnavailable',
    unreadable: 'people.failedUnreadable',
    missing: 'people.failedUnavailable',
};

const books: readonly ContactBookName[] = ['own', 'collected'];

export function ContactBook({
    book,
    reading,
    opened,
    selected,
    erasable,
    onBook,
    onOpen,
    onSelected,
    onAskErasure,
}: {
    readonly book: ContactBookName;
    readonly reading: ContactBookInForce;

    /** The person the pane beside this one is drawing, or `null` where none is. */
    readonly opened: string | null;

    /** Who is picked out, in the order this list draws them. The selection is this list's own. */
    readonly selected: readonly string[];

    /** Whether this credential may take somebody out of the book at all. */
    readonly erasable: boolean;

    readonly onBook: (book: ContactBookName) => void;
    readonly onOpen: (contact: Contact) => void;
    readonly onSelected: (selected: readonly string[]) => void;

    /** Raises the question an erasure stands behind, over the people it is about. */
    readonly onAskErasure: (contacts: readonly Contact[]) => void;
}) {
    const { locale, translate } = useLocalization();

    const [scrollTop, setScrollTop] = useState(0);
    const [viewport, setViewport] = useState(0);
    const [rowHeight, setRowHeight] = useState(estimatedRowHeight);
    const [focusedRow, setFocusedRow] = useState(0);
    const [menu, setMenu] = useState<{ readonly contact: Contact; readonly at: MenuPoint } | null>(null);

    const scroller = useRef<HTMLDivElement>(null);
    const elements = useRef(new Map<number, HTMLLIElement>());
    const wantsFocus = useRef(false);

    const contacts = reading.contacts;
    const drawn = windowOf(contacts.length, rowHeight, scrollTop, viewport);

    // The two measurements the window is arithmetic over, taken after the browser has laid the rows out rather than
    // written down as numbers here: the row's height is a token decision this must not hold a second copy of.
    useLayoutEffect(() => {
        const element = scroller.current;

        if (element === null) {
            return;
        }

        if (element.clientHeight !== viewport) {
            setViewport(element.clientHeight);
        }

        const measured = elements.current.get(drawn.first)?.offsetHeight ?? 0;

        if (measured > 0 && measured !== rowHeight) {
            setRowHeight(measured);
        }

        if (wantsFocus.current) {
            const row = elements.current.get(focusedRow);

            if (row !== undefined) {
                row.focus();
                wantsFocus.current = false;
            }
        }
    }, [viewport, rowHeight, contacts.length, drawn.first, focusedRow]);

    // A window resized changes how many rows are drawn, and a resize is not a commit.
    useEffect(() => {
        function remeasure(): void {
            setViewport(scroller.current?.clientHeight ?? 0);
        }

        window.addEventListener('resize', remeasure);

        return () => {
            window.removeEventListener('resize', remeasure);
        };
    }, []);

    function reveal(row: number): void {
        const element = scroller.current;

        if (element === null) {
            return;
        }

        const top = offsetOfRow(row, rowHeight);

        if (top < element.scrollTop) {
            element.scrollTop = top;
        } else if (top + rowHeight > element.scrollTop + element.clientHeight) {
            element.scrollTop = top + rowHeight - element.clientHeight;
        }
    }

    function moveTo(row: number): void {
        const reached = Math.min(Math.max(row, 0), Math.max(contacts.length - 1, 0));

        reveal(reached);
        setFocusedRow(reached);
        wantsFocus.current = true;
    }

    // Walking the list and acting on the row the keyboard is on, which is the whole of what the list answers a key
    // with: a row is an option and holds no control of its own, so this is where both live.
    function pressed(event: KeyboardEvent): void {
        const walked: Readonly<Record<string, number>> = {
            ArrowDown: focusedRow + 1,
            ArrowUp: focusedRow - 1,
            Home: 0,
            End: contacts.length - 1,
        };

        const reached = walked[event.key];

        if (reached !== undefined) {
            event.preventDefault();
            moveTo(reached);

            return;
        }

        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }

        const contact = contacts[focusedRow];

        if (contact === undefined) {
            return;
        }

        event.preventDefault();

        // The same two gestures the pointer has, said with the keyboard: a modifier held picks the row out, and
        // pressing a row plainly while a selection is held picks it out as well.
        if (event.ctrlKey || event.metaKey || event.shiftKey || selected.length > 0) {
            onSelected(withToggled(selected, contact.id));

            return;
        }

        onOpen(contact);
    }

    // Whether a scroll has reached the rows already read, which is what asks the deployment for the page after them.
    // It is read off the scroll rather than off a render, because a read going out is something a person's gesture
    // caused rather than something a commit should start.
    function scrolled(element: HTMLDivElement): void {
        setScrollTop(element.scrollTop);

        if (element.scrollHeight - element.scrollTop - element.clientHeight <= element.clientHeight) {
            reading.readMore();
        }
    }

    return (
        <div className="flex min-h-0 min-w-0 flex-1 flex-col">
            <div className="flex shrink-0 flex-col gap-2.25 border-b border-line px-3 py-2.75">
                <fieldset className="flex items-center gap-1 rounded-xl border border-line bg-rail p-0.75">
                    <legend className="sr-only">{translate('people.books')}</legend>

                    {books.map((name) => (
                        <ChoiceSegment
                            key={name}
                            shape="section"
                            name="contact-book"
                            value={name}
                            chosen={book === name}
                            onChoose={() => {
                                onBook(name);
                            }}
                        >
                            {translate(bookNames[name])}
                        </ChoiceSegment>
                    ))}
                </fieldset>

                <div className="flex items-baseline gap-2 px-0.75">
                    <p className="text-sm text-faint">
                        {translate(bookCounted[new Intl.PluralRules(locale).select(contacts.length)], {
                            count: new Intl.NumberFormat(locale).format(contacts.length),
                        })}
                    </p>
                    <p className="ms-auto truncate text-xs text-faint">{translate(bookHints[book])}</p>
                </div>
            </div>

            <div
                ref={scroller}
                className="min-h-0 flex-1 overflow-y-auto bg-sunken"
                onScroll={(event) => {
                    scrolled(event.currentTarget);
                }}
            >
                {/* Outside the listbox rather than inside it, because a listbox holds options and nothing else. */}
                <div aria-hidden="true" style={{ height: `${String(drawn.above)}px` }} />

                <ul
                    role="listbox"
                    aria-label={translate('people.list')}
                    aria-multiselectable={true}
                    className="flex flex-col"
                    onKeyDown={pressed}
                >
                    {Array.from({ length: drawn.count }, (_, at) => drawn.first + at).map((row) => {
                        const contact = contacts[row];

                        if (contact === undefined) {
                            return null;
                        }

                        return (
                            <ContactRow
                                key={contact.id}
                                contact={contact}
                                selected={selected.includes(contact.id)}
                                open={opened === contact.id}
                                focusable={row === focusedRow}
                                position={row + 1}
                                onOpen={() => {
                                    setFocusedRow(row);

                                    if (selected.length > 0) {
                                        onSelected(withToggled(selected, contact.id));

                                        return;
                                    }

                                    onOpen(contact);
                                }}
                                onToggle={() => {
                                    setFocusedRow(row);
                                    onSelected(withToggled(selected, contact.id));
                                }}
                                onPoint={() => {
                                    setFocusedRow(row);
                                }}
                                onPress={(at) => {
                                    setFocusedRow(row);
                                    setMenu({ contact, at });
                                }}
                                onElement={(element) => {
                                    if (element === null) {
                                        elements.current.delete(row);
                                    } else {
                                        elements.current.set(row, element);
                                    }
                                }}
                            />
                        );
                    })}
                </ul>

                <div aria-hidden="true" style={{ height: `${String(drawn.below)}px` }} />

                {reading.reading ? (
                    <p role="status" className="px-3.5 py-3 text-sm text-muted">
                        {translate('people.reading')}
                    </p>
                ) : null}

                {reading.paging ? (
                    <p role="status" className="px-3.5 py-3 text-sm text-muted">
                        {translate('people.readingMore')}
                    </p>
                ) : null}

                {reading.failure === null ? null : (
                    <div className="flex flex-col items-start gap-2 px-3.5 py-3">
                        <p role="alert" className="text-sm text-warning text-pretty">
                            {translate(bookFailures[reading.failure])}
                        </p>
                        <SecondaryButton
                            label={translate('people.readAgain')}
                            shape="compact"
                            onActivate={reading.readAgain}
                        />
                    </div>
                )}

                {!reading.reading && reading.failure === null && contacts.length === 0 ? (
                    <p className="px-3.5 py-3 text-sm text-muted text-pretty">{translate(bookEmpty[book])}</p>
                ) : null}
            </div>

            {menu === null ? null : (
                <ContactRowMenu
                    contact={menu.contact}
                    at={menu.at}
                    erasable={erasable}
                    onSelect={() => {
                        onSelected(onlySelected(menu.contact.id));
                    }}
                    onOpen={() => {
                        onOpen(menu.contact);
                    }}
                    onAskErasure={() => {
                        onAskErasure([menu.contact]);
                    }}
                    onClose={() => {
                        setMenu(null);
                        wantsFocus.current = true;
                    }}
                />
            )}
        </div>
    );
}
