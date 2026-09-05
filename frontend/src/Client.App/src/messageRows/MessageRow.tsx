// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PointerEvent, ReactNode } from 'react';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressedByFinger, useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { MessageMarkers } from '../controls/MessageMarkers';
import { Organisation } from '../controls/Organisation';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SenderAvatar } from '../controls/SenderAvatar';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { actsDrawn } from '../mailboxActs/drawnActs';
import { actPending, useMailboxActs, type MailboxAct } from '../mailboxActs/useMailboxActs';
import { drawnUnread, useReadMarking } from '../readMarking/useReadMarking';
import { useRowSwipe, type RowSwipeAct } from './rowSwipe';

// One row of mail, which is its own component for the reason a tree's row is: it is what carries state, a keyboard
// path, and a test. What it draws is what the page answered with — nothing here reads anything of its own, so a row
// costs one request for the whole page it is in.
//
// It sits beside the arithmetic that decides which rows are in the document rather than inside either screen drawing
// it, because two of them now do: the folder's list and the search's results are one row read two ways, and a second
// arrangement of the same three lines is how the client would stop looking like one product.
//
// Its height is fixed by the token rather than by its contents, and that is load-bearing rather than cosmetic: the
// window above it is arithmetic over one height, and a row that grew with a long subject would put every row below it
// somewhere other than where the list drew the space for it. The three lines are the design project's: who wrote and
// when, what about, and a line for a sentence about the message rather than from it — why a search result is in the
// list today, and what MailFathom made of the message when stage 3 lands. A row given none keeps the space, so the row
// that gains one is this row rather than a taller one.
//
// It is the one measurement in the client a composition changes, and the change is the design project's own: the same
// height at the desktop, the tablet and the fold, and a taller row with a larger circle at the phone, where the list
// is the whole screen and the row is what a thumb lands on. Both come out of the same tree at the same breakpoint, so
// the row that grows is this row and not a second one — and the window above it reads what was drawn rather than the
// token, which is what lets the height change at all.

// What the row says while a change this client asked for has not been seen to have reached the mail server. A mailbox
// mutation is durable the moment it is written down and converges minutes later, so a row that said nothing would leave
// somebody pressing archive twice; the sentence goes on its own once the change has arrived.
const actPendingSaid: Readonly<Record<MailboxAct, MessageKey>> = {
    flag: 'act.flagging',
    markUnread: 'act.markingUnread',
    archive: 'act.archiving',
    delete: 'act.deleting',
    move: 'act.filing',
};

// What each direction of a swipe shows behind the row it is carrying, which is the design project's own: the act the
// finger has asked for, named and drawn, against the edge it is uncovering. Filing takes its name and its symbol from
// `mailboxActs/drawnActs.ts` rather than from a second table here, so a swipe says what the row's menu and the toolbar
// say; answering is not one of the five acts and names its own.
const swipeDrawn: Readonly<
    Record<RowSwipeAct, { readonly icon: IconName; readonly said: MessageKey; readonly tint: string }>
> = {
    answer: { icon: 'reply', said: 'mail.reply', tint: 'justify-end pe-5.5 bg-accent-soft text-accent-strong' },
    archive: {
        icon: actsDrawn.archive.icon,
        said: actsDrawn.archive.label,
        tint: 'justify-start ps-5.5 bg-healthy-soft text-healthy-text',
    },
};

export function MessageRow({
    email,
    position,
    open,
    selected,
    focusable,
    note,
    onOpen,
    onPoint,
    onPress,
    onAnswer,
    onArchive,
    onPointerEnter,
    onElement,
}: {
    readonly email: MailTimelineEntry;
    readonly position: number;
    readonly open: boolean;
    readonly selected: boolean;
    readonly focusable: boolean;

    /** What the row has to say about the message beyond what it draws, in the line the height already reserves. */
    readonly note?: ReactNode;
    readonly onOpen: () => void;

    /**
     * What pointing at this row means: a mouse pressed on it, or a finger lifted off it having only tapped.
     *
     * The two arrive at different moments and that is the whole of what a press costs the row. A mouse acts as it goes
     * down, because the same press may go on to sweep a run of rows; a finger's press is not decided until it is
     * lifted, since the same touch may become the long press that opens this row's menu — and a row that had already
     * opened its message would put that menu over something nobody asked to see.
     */
    readonly onPoint: (event: PointerEvent<HTMLLIElement>) => void;

    /**
     * Opens this row's menu at the point the gesture happened, or absent for a list that offers none.
     *
     * A row without it keeps the browser's own menu under a pointer and answers a held finger with nothing, which is
     * what the search results are: a result is opened rather than acted on, and a menu of acts over a selection that
     * list does not model would offer what it cannot do.
     */
    readonly onPress?: (at: MenuPoint) => void;

    /**
     * Opens the message and starts an answer to it, which is what a finger swiped left across the row asks for.
     *
     * Absent where this list cannot answer — a deployment that refuses a draft, or the search's results — and the row
     * then springs back from a leftward swipe rather than following the finger toward an act nobody would see happen.
     */
    readonly onAnswer?: (() => void) | undefined;

    /** Files the message away, which is what a finger swiped right asks for. Absent on the same terms as `onAnswer`. */
    readonly onArchive?: (() => void) | undefined;

    readonly onPointerEnter: () => void;
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { translate } = useLocalization();
    const marking = useReadMarking();
    const acts = useMailboxActs();
    const press = useRowPress(onPress);
    const swipe = useRowSwipe(press, { answer: onAnswer, archive: onArchive });

    // The act this row is still waiting on. It is what the reserved line says while it stands, ahead of whatever the
    // screen would otherwise put there: a message on its way out of the folder is the more urgent fact about the row
    // than why a search found it.
    const acting = actPending(acts, email);

    // What the deployment last reported, less what this client has marked read since. The two are not the same for
    // minutes at a time: marking read is a durable mutation the account's own pass carries to the mail server, and the
    // stored flag is an observation of what that server was seen to hold — so the row draws from the pending mutation
    // rather than waiting for the observation to catch up.
    //
    // The row stays in the list either way, including a list narrowed to unread mail. A message drawn read is a message
    // the person is reading; taking its row out from under them the moment the pane rendered it would be the list
    // disagreeing with the pane about what is open, and the next read of the folder is what removes it.
    //
    // A message this client has just asked to be marked unread is drawn unread from the press, for the same reason and
    // in the other direction: the two statements are one pending mutation each, and the row draws from whichever of
    // them was asked for last.
    const unread = acting === 'markUnread' || drawnUnread(marking, email.id, email.unread);

    // What is showing behind the row while a finger carries it, or nothing for a row standing where the list drew it.
    // Which of the two it is is the direction alone: what the threshold decides is how firmly it is drawn rather than
    // which act it names, so somebody who has begun a swipe can read what it is for before they have finished it.
    const carrying = swipe.carried === 0 ? undefined : swipeDrawn[swipe.carried < 0 ? 'answer' : 'archive'];

    return (
        <li
            ref={onElement}
            role="option"
            aria-selected={selected}
            aria-posinset={position}
            // The list is keyset-paged, so how many rows the folder holds is not something any page answers. That is
            // what ARIA's unknown size names, and it is the accurate answer rather than the number of rows held.
            aria-setsize={-1}
            aria-current={open ? 'true' : undefined}
            tabIndex={focusable ? 0 : -1}
            onContextMenu={press.onContextMenu}
            onPointerDown={(event) => {
                press.onPointerDown(event);
                swipe.onPointerDown(event);

                // The primary button alone acts. The second one is what asks the row what it offers, and a row that
                // also selected the message and opened it under the menu would be answering a question with an act.
                if (!pressedByFinger(event.pointerType) && event.button === 0) {
                    onPoint(event);
                }
            }}
            onPointerMove={(event) => {
                // The press first, because it is the one that gives way: it is off at a shorter travel than the swipe
                // needs to engage, so a finger that has begun to carry the row has already stopped arming a menu.
                press.onPointerMove(event);
                swipe.onPointerMove(event);
            }}
            onPointerUp={(event) => {
                // The tap this lift amounts to, read before the press is cleared so that a lift ending a press which
                // has opened the menu acts on nothing. The swipe is asked afterwards rather than before, because the
                // lift is what finishes one — and a lift that has just filed the message away is not also a tap on it.
                const tapped = pressedByFinger(event.pointerType) && !press.tapSuppressed();

                press.onPointerUp();
                swipe.onPointerUp(event);

                if (tapped && !swipe.tapSuppressed()) {
                    onPoint(event);
                }
            }}
            onPointerCancel={() => {
                press.onPointerCancel();
                swipe.onPointerCancel();
            }}
            onPointerEnter={onPointerEnter}
            onDoubleClick={onOpen}
            // Flush and square rather than a card: the rows are one continuous list, each separated from the next by
            // the line it carries, which is what the window's arithmetic needs them to be as well. The line and the
            // height are the outer element's rather than the carried one's, so a row travelling under a finger keeps
            // the space the window drew for it and is clipped at the column's edges rather than crossing them.
            //
            // Vertical panning stays the scroller's and everything sideways is the row's, which is what stops a browser
            // from taking the gesture over as a scroll before it has been read.
            className="relative h-message-row-narrow touch-pan-y overflow-hidden border-b border-b-sunken workspace:h-message-row"
        >
            {carrying === undefined ? null : (
                // What the row is being carried off is showing: the act, named and drawn, against the edge the finger
                // has uncovered. Hidden from the accessibility tree because it says what a gesture is about to do, and
                // nothing here is reachable by a gesture alone — the same two acts are on the row's own menu and in
                // the toolbar, which is where a keyboard and a screen reader meet them.
                //
                // Faint until the threshold is crossed and full once it is, which is how the design project says the
                // finger has gone far enough. It draws that partly by thickening the symbol's stroke, which a set of
                // committed outlines has no equivalent for, so both the symbol and the word answer to the one signal
                // this client can draw.
                <span
                    aria-hidden="true"
                    className={`absolute inset-0 flex items-center ${carrying.tint} ${
                        swipe.commits === null ? 'opacity-55' : 'opacity-100'
                    }`}
                >
                    <Icon name={carrying.icon} className="me-2 size-5.25" />

                    <span className="text-base font-semibold">{translate(carrying.said)}</span>
                </span>
            )}

            <div
                // The row that is open, and the rows picked out for a question, are marked at the edge rather than by
                // a ring around them, which is the design project's mark and keeps the row's own lines where they
                // were.
                className={`flex h-full cursor-pointer flex-col justify-center gap-0.75 overflow-hidden border-s-4 ps-2.5 pe-3.5 ${
                    swipe.carried === 0 ? 'transition' : ''
                } ${
                    selected
                        ? 'border-s-accent bg-accent-soft'
                        : open
                          ? 'border-s-accent-line bg-accent-soft'
                          : 'border-s-transparent bg-panel hover:bg-hover'
                }`}
                // The one value here a token cannot hold: how far this row has been carried is a distance a finger
                // decided rather than a decision the theme took.
                style={swipe.carried === 0 ? undefined : { transform: `translateX(${String(swipe.carried)}px)` }}
            >
                <div className="flex items-center gap-2">
                    {/* Unread is drawn as weight and colour, which is how the design project draws it, and said in as
                        many words for a reader who is not looking at either. Nothing marks the row visually beside
                        that: a dot ahead of the avatar would inset every unread row a little further than every read
                        one, which is the one thing a list of rows that have to scan as a column cannot afford. */}
                    {unread ? <span className="sr-only">{translate('list.unread')}</span> : null}

                    <SenderAvatar displayName={email.senderDisplayName} address={email.senderAddress} place="row" />

                    {/* Who wrote is read before where they wrote from, so the name keeps up to half the line and the
                        host is what gives way. Half rather than more because the marks and the time hold their own
                        width: a name allowed past it would push the time out of a row that clips rather than wraps. */}
                    <span
                        className={`max-w-1/2 shrink-0 truncate text-md font-semibold ${unread ? 'text-text' : 'text-text-soft'}`}
                    >
                        {correspondent(email) ?? translate('list.senderUnknown')}
                    </span>

                    <Organisation address={email.senderAddress} />

                    <MessageMarkers email={email} />

                    <ReceivedAt at={email.receivedAt} />
                </div>

                <div className={`truncate text-md ${unread ? 'text-text' : 'text-text-soft'}`}>
                    {email.subject ?? translate('list.noSubject')}
                </div>

                {/* The reserved line. Hidden from the accessibility tree where it holds nothing, so a row with nothing
                    to say about itself is not announced as one with an empty line in it. */}
                <div
                    aria-hidden={acting === null && note === undefined ? 'true' : undefined}
                    className="h-4 overflow-hidden text-xs text-muted"
                >
                    {acting === null ? note : translate(actPendingSaid[acting])}
                </div>
            </div>
        </li>
    );
}

/** Who the row is about: the sender, else the address it came from, else who it was written to. */
function correspondent(email: MailTimelineEntry): string | undefined {
    return email.senderDisplayName ?? email.senderAddress ?? email.toAddresses[0];
}
