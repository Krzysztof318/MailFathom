// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import {
    useEffect,
    useLayoutEffect,
    useRef,
    useState,
    type KeyboardEvent,
    type PointerEvent,
    type ReactNode,
} from 'react';
import {
    readMailTimeline,
    type ClientFailure,
    type ClientFailureReason,
    type ClientSession,
    type MailAccount,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { CheckControl } from '../controls/CheckControl';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { needsAttention } from '../synchronization/synchronizationState';
import { accountInScope, type MailScope } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import {
    answered,
    cursorAfter,
    heldRows,
    nothingHeld,
    positionOfRow,
    rowAt,
    rowCountOf,
    trimmedAround,
    wantedFor,
    type HeldTimeline,
    type TimelineRead,
} from './heldTimeline';
import { ListSettings } from './ListSettings';
import { narrowed, queryFor, type MailListing } from './listing';
import { MessageRow } from './MessageRow';
import { extendedTo, inReadingOrder, onlySelected, withToggled } from './messageSelection';
import { rememberedListing, rememberListing } from './rememberedListings';
import { estimatedRowHeight, leadingRow, offsetOfRow, windowOf } from './timelineWindow';

// The client's message list, which is where a mail client is judged: it stays smooth at message forty thousand, it puts
// a returning reader back where they were, and it lets somebody pick out four messages for the question they are about
// to ask.
//
// Three bounds hold that up and none of them is optional. The document holds a window of rows rather than the folder —
// `timelineWindow.ts`. The list holds a window of pages rather than every page it has read — `heldTimeline.ts`. And
// where the reader is survives outside React rather than in it — `rememberedListings.ts`, because a scroll offset in
// state re-renders everything under the workspace provider on the one interaction this screen exists to keep smooth.
//
// The component is mounted with the scope as its key, so pointing at another mailbox starts a list rather than resets
// one: every piece of state below belongs to one folder read one way, and there is no correct way to carry any of it
// across.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

// How long after scrolling stops before where the reader is is written down. Long enough that a flick through a folder
// writes once rather than per frame, short enough that leaving immediately afterwards keeps the position.
const restingBeforeRemembered = 400;

export function MessageList({
    session,
    transport,
    scope,
    accounts,
    online,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly scope: MailScope;
    readonly accounts: readonly MailAccount[];
    readonly online: boolean;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    // Where this list opens: how the folder was last read and where in it the reader was. State rather than a value
    // computed each render, because it is read back once the first page has arrived and because changing the order or
    // a filter replaces it with the leading end of the list the change asks for.
    const [opening, setOpening] = useState(() => rememberedListing(session.baseAddress, scope));

    const [listing, setListing] = useState<MailListing>(opening);
    const [held, setHeld] = useState<HeldTimeline>(nothingHeld);
    const [failure, setFailure] = useState<ClientFailure | null>(null);

    const [scrollTop, setScrollTop] = useState(0);
    const [viewport, setViewport] = useState(0);
    const [rowHeight, setRowHeight] = useState(estimatedRowHeight);

    const [focusedRow, setFocusedRow] = useState(0);
    const [anchor, setAnchor] = useState<string | null>(null);
    const [selecting, setSelecting] = useState(false);

    const scroller = useRef<HTMLDivElement>(null);
    const elements = useRef(new Map<number, HTMLLIElement>());
    const dragging = useRef(false);
    const restored = useRef(false);
    const wantsFocus = useRef(false);

    const rowCount = rowCountOf(held);
    const drawn = windowOf(rowCount, rowHeight, scrollTop, viewport);
    const lastDrawn = drawn.first + drawn.count - 1;
    const rows = heldRows(held);

    // Whether the row the keyboard is on is a row rather than the space one is arriving into. The effect below waits on
    // it: a keyboard that reached a dropped page has nothing to put focus on until that page answers, and refilling one
    // changes neither the row count nor the window — so this is what says the row is there now.
    const focusedIsDrawn = rowAt(held, focusedRow) !== null;

    // Which page is wanted is worked out during render rather than kept beside what is held, because it is a function
    // of what is held and where the window is: two pieces of state that have to agree are one piece of state and a
    // function, and the pair that disagrees here would be a list reading a page it already has.
    //
    // It is `null` for a list that wants nothing, which is also how a read in flight reads: the answer is what changes
    // it, so nothing has to be kept saying whether one is out. A network gap and a failure both make it `null`, which
    // ends the read they interrupted rather than letting it outlive them.
    const wanted =
        !online || failure !== null
            ? null
            : held.slots.length === 0
              ? { cursor: opening.cursor, direction: opening.readAs, refilling: null }
              : wantedFor(held, drawn.first, lastDrawn);

    // Named apart so the effect below depends on what the request *is* rather than on the object naming it. A fresh
    // object every render would put a request on the wire every render; these three change only when the page wanted
    // changes, which is what starts one read per page and abandons one the reader has scrolled away from.
    const wantedCursor = wanted?.cursor ?? null;
    const wantedDirection = wanted?.direction ?? null;
    const wantedRefilling = wanted?.refilling ?? null;

    // The one effect that puts a request on the wire, which is what an effect is for. What it asked for travels with
    // the answer, so a page knows where it belongs and an answer to a read this list has moved on from is discarded.
    useEffect(() => {
        if (wantedDirection === null) {
            return;
        }

        const asked: TimelineRead = {
            cursor: wantedCursor,
            direction: wantedDirection,
            refilling: wantedRefilling,
        };

        let listening = true;

        void readMailTimeline(session, transport, queryFor(scope, listing, asked.cursor, asked.direction)).then(
            (result) => {
                if (!listening) {
                    return;
                }

                if (result.outcome === 'failed') {
                    setFailure(result.failure);
                } else {
                    setHeld((current) => answered(current, result.value, asked));
                }
            },
        );

        return () => {
            listening = false;
        };
    }, [session, transport, scope, listing, wantedCursor, wantedDirection, wantedRefilling]);

    // Where the reader is, written down once they have stopped moving, and again the moment the page goes away —
    // whichever comes first. The second is what makes a reload a continuation rather than a race with the first: a
    // reader who reloads, closes the tab, or is sent to another page mid-scroll keeps where they were. Outside React
    // both times, so nothing here re-renders.
    useEffect(() => {
        function keep(): void {
            const position = positionOfRow(held, leadingRow(scrollTop, rowHeight));

            if (position !== null) {
                rememberListing(session.baseAddress, scope, { ...listing, ...position });
            }
        }

        const timer = window.setTimeout(keep, restingBeforeRemembered);
        window.addEventListener('pagehide', keep);

        return () => {
            window.clearTimeout(timer);
            window.removeEventListener('pagehide', keep);
        };
    }, [held, scrollTop, rowHeight, listing, scope, session.baseAddress]);

    // The two measurements the window is arithmetic over, taken after the browser has laid the list out rather than
    // written down as numbers here. One element each, on a commit that has already happened: the row height is a token
    // decision this must not hold a second copy of, and the scroller's height is whatever the composition gave it.
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

        // The reader is put back where they were once there is something to put them back into, and once only: every
        // later scroll is theirs. The measured height rather than the one in state, because both happen on this commit
        // and the one in state is a render behind.
        if (!restored.current && rowCount > 0) {
            restored.current = true;
            element.scrollTop = offsetOfRow(opening.rowInPage, measured > 0 ? measured : rowHeight);
        }

        if (wantsFocus.current && focusedIsDrawn) {
            const row = elements.current.get(focusedRow);

            if (row !== undefined) {
                row.focus();
                wantsFocus.current = false;
            }
        }
    }, [viewport, rowHeight, rowCount, drawn.first, opening.rowInPage, focusedRow, focusedIsDrawn]);

    // A window resized changes how many rows the list draws, and a resize is not a commit. The scroller's own size is
    // read on the commit that follows, which is what this asks for.
    useEffect(() => {
        function remeasure(): void {
            setViewport(scroller.current?.clientHeight ?? 0);
        }

        window.addEventListener('resize', remeasure);

        return () => {
            window.removeEventListener('resize', remeasure);
        };
    }, []);

    // A drag selects while the pointer is down and stops wherever it is let go, including outside the list — a drag
    // that ended over the header would otherwise still be selecting when the pointer came back.
    useEffect(() => {
        function release(): void {
            dragging.current = false;
        }

        window.addEventListener('pointerup', release);
        window.addEventListener('pointercancel', release);

        return () => {
            window.removeEventListener('pointerup', release);
            window.removeEventListener('pointercancel', release);
        };
    }, []);

    // Scrolling is where the list stops holding what the reader has moved away from. Here rather than in an effect
    // watching the window, because dropping rows is what a scroll did rather than something to reconcile afterwards —
    // and `trimmedAround` answers with the list it was given where nothing was far enough to drop, so the ordinary
    // scroll that changes nothing renders nothing.
    function scrolledTo(top: number): void {
        setScrollTop(top);

        const moved = windowOf(rowCount, rowHeight, top, viewport);

        setHeld((current) => trimmedAround(current, moved.first, moved.first + moved.count - 1));
    }

    function tryAgain(): void {
        setFailure(null);
    }

    function select(selected: readonly string[]): void {
        revise({ selected: inReadingOrder(selected, rows.map(identityOf)) });
    }

    function readWith(chosen: MailListing): void {
        // The cursor belongs to the order and the filters it was issued under, so changing either starts the list at
        // its leading end rather than continuing from a cursor the deployment would refuse.
        const restarted = { ...chosen, cursor: null, readAs: 'forward' as const, rowInPage: 0 };

        // Written down here rather than left to the effect below, because how a folder is read is a choice somebody
        // made rather than a position they drifted to: leaving the moment after making it keeps it.
        rememberListing(session.baseAddress, scope, restarted);
        setOpening(restarted);
        restored.current = false;
        setListing(chosen);
        setHeld(nothingHeld);
        setFailure(null);
        setFocusedRow(0);
    }

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

    function moveTo(row: number, extending: boolean, keepingSelection: boolean): void {
        const reached = Math.min(Math.max(row, 0), Math.max(rowCount - 1, 0));

        reveal(reached);
        setFocusedRow(reached);
        wantsFocus.current = true;

        const email = rowAt(held, reached);

        if (email === null || keepingSelection) {
            return;
        }

        if (extending && anchor !== null) {
            select(extendedTo(workspace.selected, rows.map(identityOf), anchor, email.id));
        } else {
            setAnchor(email.id);
            select(onlySelected(email.id));
        }
    }

    function open(row: number): void {
        const email = rowAt(held, row);

        if (email !== null) {
            revise({ selection: email.id });
        }
    }

    function point(event: PointerEvent<HTMLLIElement>, row: number): void {
        const email = rowAt(held, row);

        if (email === null) {
            return;
        }

        setFocusedRow(row);

        // Adding one at a time, which is what the modifier key does under a pointer and what the selection mode does
        // under a finger. The mode exists because a touch screen has no modifier, and it is a control rather than a
        // gesture so that its state is something a reader can see and a screen reader can say.
        if (selecting || event.ctrlKey || event.metaKey) {
            setAnchor(email.id);
            select(withToggled(workspace.selected, email.id));

            return;
        }

        if (event.shiftKey && anchor !== null) {
            select(extendedTo(workspace.selected, rows.map(identityOf), anchor, email.id));

            return;
        }

        dragging.current = true;
        setAnchor(email.id);
        select(onlySelected(email.id));
        revise({ selection: email.id });
    }

    function dragOver(row: number): void {
        const email = rowAt(held, row);

        if (!dragging.current || email === null || anchor === null) {
            return;
        }

        select(extendedTo(workspace.selected, rows.map(identityOf), anchor, email.id));
    }

    function onKeyDown(event: KeyboardEvent<HTMLUListElement>): void {
        switch (event.key) {
            case 'ArrowDown':
                moveTo(focusedRow + 1, event.shiftKey, event.ctrlKey || event.metaKey);
                break;
            case 'ArrowUp':
                moveTo(focusedRow - 1, event.shiftKey, event.ctrlKey || event.metaKey);
                break;
            case 'Home':
                moveTo(0, event.shiftKey, event.ctrlKey || event.metaKey);
                break;
            case ' ': {
                const email = rowAt(held, focusedRow);

                if (email !== null) {
                    setAnchor(email.id);
                    select(withToggled(workspace.selected, email.id));
                }

                break;
            }
            case 'Enter':
                open(focusedRow);
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    if (!online) {
        return <Note>{translate('connection.offline')}</Note>;
    }

    if (rowCount === 0 && failure !== null) {
        return (
            <div className="flex flex-col items-start gap-2">
                {/* Announced rather than merely drawn, for the reason the reading pane's failure is: a reader who
                    heard the list say it was reading hears nothing at all when it stops, and the way out sits under a
                    sentence they were never told about. */}
                <p className="text-sm text-warning" role="alert">
                    {translate('list.failed', { reason: translate(failureLabels[failure.reason]) })}
                </p>

                {/* Reading again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
                {failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={tryAgain} />
                ) : null}
            </div>
        );
    }

    if (rowCount === 0 && wanted !== null) {
        return <Note announced>{translate('list.reading')}</Note>;
    }

    return (
        <div className="flex min-h-0 flex-col gap-2">
            <ListSettings
                listing={listing}
                junkAskable={scope.kind !== 'folder' && scope.kind !== 'role'}
                onRead={readWith}
            />

            <div className="flex flex-wrap items-center gap-2">
                {/* Under a finger there is no modifier key, so picking out several messages is a mode rather than a
                    chord. It is offered at every width and to every pointer, because a mode a reader can see is easier
                    than a chord they have to know — and because nothing in this tree asks which head it runs on. */}
                <CheckControl
                    label={translate('list.selectSeveral')}
                    on={selecting}
                    onChange={(on) => {
                        setSelecting(on);
                    }}
                />

                {workspace.selected.length === 0 ? null : (
                    <p className="text-sm text-muted" role="status">
                        {translate('list.selectedCount', { count: String(workspace.selected.length) })}
                    </p>
                )}

                {/* A read that failed with rows already drawn is the partial state: what is on the screen stays, and
                    what is missing is said above it rather than replacing it. */}
                {failure === null ? null : (
                    <p className="text-sm text-warning" role="alert">
                        {translate('list.partiallyFailed', { reason: translate(failureLabels[failure.reason]) })}
                    </p>
                )}

                {failure?.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={tryAgain} />
                ) : null}
            </div>

            {rowCount === 0 ? (
                <Note>{translate(emptyReason(accounts, scope, listing))}</Note>
            ) : (
                <div
                    ref={scroller}
                    className="h-message-list min-h-0 overflow-y-auto overscroll-contain"
                    onScroll={(event) => {
                        scrolledTo(event.currentTarget.scrollTop);
                    }}
                >
                    {/* The rows that are not in the document, as the space they take. Outside the list rather than in
                        it, because a listbox holds options and nothing else — a spacer inside it would be announced as
                        one more thing in the list. */}
                    <div aria-hidden="true" style={{ height: `${String(drawn.above)}px` }} />

                    {/* Rows sit flush against each other, and that is arithmetic rather than taste: the spacers above
                        and below are whole rows of the token height, so a gap between drawn rows would put every row a
                        fraction lower than the space the list drew for it, and the drift would grow with how far down
                        the folder the reader is. What separates them is the line each row carries. */}
                    <ul
                        aria-label={translate('list.label')}
                        aria-busy={wanted !== null}
                        aria-multiselectable="true"
                        role="listbox"
                        className="flex flex-col"
                        onKeyDown={onKeyDown}
                    >
                        {Array.from({ length: drawn.count }, (_, at) => drawn.first + at).map((row) => {
                            const email = rowAt(held, row);

                            return email === null ? (
                                <ArrivingRow key={`arriving-${String(row)}`} position={row + 1} />
                            ) : (
                                <MessageRow
                                    key={email.id}
                                    email={email}
                                    position={row + 1}
                                    open={workspace.selection === email.id}
                                    selected={workspace.selected.includes(email.id)}
                                    focusable={row === focusedRow}
                                    onOpen={() => {
                                        open(row);
                                    }}
                                    onPoint={(event) => {
                                        point(event, row);
                                    }}
                                    onPointerEnter={() => {
                                        dragOver(row);
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

                    {wanted !== null ? (
                        <p className="px-3 py-2 text-sm text-muted" role="status">
                            {translate('list.readingMore')}
                        </p>
                    ) : null}

                    {cursorAfter(held) === null ? (
                        <p className="px-3 py-2 text-sm text-faint">{translate('list.wholeFolderRead')}</p>
                    ) : null}
                </div>
            )}
        </div>
    );
}

function identityOf(email: { readonly id: string }): string {
    return email.id;
}

/**
 * Why a folder is showing nothing, which is four different sentences rather than one.
 *
 * A folder nothing has been taken into yet is the one a reader would otherwise read as empty and act on — so it is told
 * apart from a folder that genuinely holds nothing, from one whose account stopped synchronizing, and from a list the
 * reader has narrowed to nothing themselves.
 */
function emptyReason(accounts: readonly MailAccount[], scope: MailScope, listing: MailListing): MessageKey {
    const named = accountInScope(scope);
    const inScope = named === null ? accounts : accounts.filter((account) => account.id === named);

    if (inScope.length > 0 && inScope.every((account) => account.synchronizationState === 'NeverSynchronized')) {
        return 'list.notSynchronizedYet';
    }

    if (inScope.some((account) => needsAttention(account.synchronizationState))) {
        return 'list.emptyWhileFailing';
    }

    return narrowed(listing.filters) ? 'list.nothingMatches' : 'list.emptyFolder';
}

// A row standing where a page the list dropped used to be. It is drawn rather than left blank so that the space is
// visibly a message on its way rather than a hole, and it takes exactly the room the row it stands for will.
function ArrivingRow({ position }: { readonly position: number }) {
    const { translate } = useLocalization();

    return (
        <li
            role="option"
            aria-selected={false}
            aria-disabled="true"
            aria-posinset={position}
            aria-setsize={-1}
            className="flex h-message-row items-center px-3 text-sm text-faint"
        >
            {translate('list.rowArriving')}
        </li>
    );
}

function Note({ announced = false, children }: { readonly announced?: boolean; readonly children: ReactNode }) {
    return (
        <p className="text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}
