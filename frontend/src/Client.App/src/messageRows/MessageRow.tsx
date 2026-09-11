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
import {
    actPending,
    changesAFlag,
    drawnFlagged,
    useMailboxActs,
    type AskedAct,
    type FilingAct,
} from '../mailboxActs/useMailboxActs';
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
//
// **Only the acts that file a message elsewhere say anything.** What a flag act does is a mark this row draws from the
// press — the flag, or the unread dot — so the outcome is already on the screen, and a sentence beside it saying the
// mark is on its way would be the row narrating a mechanism instead of showing a state. The design draws the mark and
// no sentence, which is also why nothing reports one in the corner: `mailboxActs/MailboxActs.tsx` says that half.
const actPendingSaid: Readonly<Record<FilingAct, MessageKey>> = {
    archive: 'act.archiving',
    delete: 'act.deleting',
    move: 'act.filing',
};

// Deleting is the one act whose sentence turns on what it does rather than on its name. Sent to the trash it files the
// message somewhere its reader can go and fetch it, and the row leaves the folder saying so; performed on a message
// already in the trash it destroys the mail, so the row stays where it is for the seconds in which the deployment is
// still holding the change back — and it says *that*, because a row reading `Moving to the trash…` in the trash would
// be describing an act that is not the one about to happen.
//
// It reads what the act destroys rather than whether the row is leaving, because the two stop agreeing at the exact
// moment somebody is watching: the way back closes, the released delete takes the row out of the list, and a sentence
// read off the leaving would turn into `Moving to the trash…` over a message being destroyed — on the last frames
// anybody sees of it.
function actPendingWording(asked: AskedAct): MessageKey | null {
    if (changesAFlag(asked.act)) {
        return null;
    }

    return asked.destroys ? 'act.deletingPermanently' : actPendingSaid[asked.act];
}

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
        tint: 'justify-start ps-5.5 bg-warning-soft text-warning-text',
    },
};

export function MessageRow({
    email,
    position,
    open,
    selected,
    focusable,
    arrived,
    changed,
    note,
    onReadings,
    onOpen,
    onPoint,
    onPress,
    onAnswer,
    onArchive,
    onPointerEnter,
    onSettled,
    onGone,
    onElement,
}: {
    readonly email: MailTimelineEntry;
    readonly position: number;
    readonly open: boolean;
    readonly selected: boolean;
    readonly focusable: boolean;

    /**
     * Whether this row arrived in a list the reader was already looking at, which is what it lands for. It is false
     * for every row of a list's first read: that list appeared, and nothing arrived in it.
     */
    readonly arrived?: boolean;

    /**
     * Whether the deployment named this message as one that changed, which is what the row is washed and marked for.
     * A list whose rows nothing signals about — the search results — hands neither.
     */
    readonly changed?: boolean;

    /** What the row has to say about the message beyond what it draws, in the line the height already reserves. */
    readonly note?: ReactNode;

    /**
     * Opens what MailFathom read from this message, or absent where it read nothing from it and where the list offers
     * no such surface at all.
     */
    readonly onReadings?: (() => void) | undefined;
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

    /**
     * That the row has finished saying it moved, so the list stops holding it as a row that did.
     *
     * A windowed list unmounts a row scrolled out of the window and mounts it again on the way back, and a row still
     * held as arrived would land a second time each time that happened — which is the animation running for something
     * that did not just happen. So the row reports the end of its own animation rather than the list timing it, and
     * the duration stays in the stylesheet where every other one is.
     */
    readonly onSettled?: (() => void) | undefined;

    /**
     * That the row has finished going, so the list stops drawing it at all.
     *
     * The other half of `onSettled` and reported from the same event, because what a row's animation ending means
     * depends on which animation it was: a row that landed or was washed is still in the list, and a row that went out
     * under an act has left it. The list holds it for exactly as long as that animation, for the reason the row
     * reports the end of one rather than the list timing it.
     */
    readonly onGone?: (() => void) | undefined;

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
    // A message this client has just asked to be marked unread is drawn unread from the press, and one asked to be
    // marked read is drawn read from it, which is the same rule in both directions.
    const unread =
        acting?.act === 'markUnread' || (acting?.act !== 'markRead' && drawnUnread(marking, email.id, email.unread));

    // The flag the row draws, which is the mark either flag act reports and the whole of what it reports.
    const flagged = drawnFlagged(acts, email);

    // Which animation this row is going out on, or nothing for a row standing where the list drew it. Only an act that
    // takes the message out of the folder it is drawn in goes out at all, and the act is what names the colour: the
    // design draws red for a deletion and orange for filing a message somewhere else, archive and move alike. A row
    // that goes because the folder was read again is not held while it goes and plays neither.
    //
    // **The colour is drawn on what the row carries and the leaving on the row**, which is two animations on two
    // elements and is forced rather than chosen: the wash is an inset shadow, and one inset into the row is painted
    // underneath the opaque element the row carries, so it is invisible for the whole animation and what a reader
    // sees is a row travelling and fading without ever changing colour. The leaving stays on the row because that is
    // where it is reported from — `onAnimationEnd` below reads the row's own animation and nothing inside it.
    //
    // Nothing lands on the row while it goes, which is the design project's own: a row already out of the folder is
    // not a row to open, and the half-second it is still drawn for is exactly long enough to be clicked on by
    // accident.
    const wash = !acting?.leaves ? null : acting.act === 'delete' ? 'animate-row-deleted' : 'animate-row-filed';
    const going = wash === null ? null : 'pointer-events-none animate-row-going';

    // What the reserved line holds: what the act says about itself where it says anything, else whatever the screen
    // would otherwise put there. Worked out here rather than in the markup, because the line is also hidden from the
    // accessibility tree where it holds nothing, and two readings of *nothing* is how one of them comes to be wrong.
    const pendingSaid = acting === null ? null : actPendingWording(acting);
    const said = pendingSaid === null ? note : translate(pendingSaid);

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
            onAnimationEnd={(event) => {
                // The row's own animation and not one inside it: `animationend` bubbles, so a symbol animating within
                // the row would otherwise report the row as having finished moving before it had.
                //
                // Which of the two it reports is decided by which animation the row is drawing rather than by the
                // event's own name: at most one of them is on the row at a time, and reading the class the row chose
                // is the same answer without a second place for the two to disagree about an animation's name.
                if (event.target !== event.currentTarget) {
                    return;
                }

                if (going === null) {
                    onSettled?.();
                } else {
                    onGone?.();
                }
            }}
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
            //
            // One animation at most, and going wins over both of the others: a row on its way out of the folder is not
            // also arriving in it or changing in place. Between those two the arrival wins, because a row that has only
            // just been drawn has nothing to have changed from, so washing it as well would be marking it against a
            // version of itself the reader never saw. All three are the design project's, and `styles.css` holds why
            // neither the arrival nor the going here carries the height a flowing list's does.
            className={`relative h-message-row-narrow touch-pan-y overflow-hidden border-b border-b-sunken workspace:h-message-row ${
                going ?? (arrived === true ? 'animate-row-landing' : changed === true ? 'animate-row-changed' : '')
            }`}
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
                    wash ?? ''
                } ${swipe.carried === 0 ? 'transition' : ''} ${
                    selected
                        ? 'border-s-accent bg-accent-soft'
                        : open
                          ? 'border-s-accent-strong bg-accent-soft'
                          : 'border-s-transparent bg-panel hover:bg-hover'
                }`}
                // The one value here a token cannot hold: how far this row has been carried is a distance a finger
                // decided rather than a decision the theme took.
                style={swipe.carried === 0 ? undefined : { transform: `translateX(${String(swipe.carried)}px)` }}
            >
                <div className="flex items-center gap-2">
                    <SenderAvatar displayName={email.senderDisplayName} address={email.senderAddress} place="row" />

                    {/* Who wrote is read before where they wrote from, so the name keeps up to half the line and the
                        host is what gives way. Half rather than more because the marks and the time hold their own
                        width: a name allowed past it would push the time out of a row that clips rather than wraps. */}
                    <span className="max-w-1/2 shrink-0 truncate text-md font-semibold">
                        {correspondent(email) ?? translate('list.senderUnknown')}
                    </span>

                    <Organisation address={email.senderAddress} />

                    <MessageMarkers email={email} flagged={flagged} />

                    {/* Unread, and the whole of what says so. The design project draws the mark here — at the end of
                        the marks, between the flag and the time, rather than ahead of the avatar, where it would inset
                        every unread row a little further than every read one, which is the one thing a column that has
                        to scan cannot afford — and draws the name and the subject of a read row in the same weight and
                        the same colour as an unread one. So the dot is the mark rather than a second statement beside
                        a typographic one, and the words beside it are for a reader who is not looking at it. */}
                    {unread ? (
                        <span className="shrink-0">
                            <span aria-hidden="true" className="block size-2 rounded-full bg-accent" />
                            <span className="sr-only">{translate('list.unread')}</span>
                        </span>
                    ) : null}

                    {/* How long the correspondence is, drawn where it is longer than the one message. The design
                        project puts it between the unread mark and the time and draws nothing at all for a message
                        standing alone, so the pill says this row stands for an exchange rather than repeating what
                        every row already is. The number is the deployment's count over the whole conversation — a
                        client counting the rows it holds would answer differently depending on where the page was
                        cut — and the words beside it are for a reader who is not looking at it, which the design has
                        no way to draw. */}
                    {email.threadMessageCount !== null && email.threadMessageCount > 1 ? (
                        <span className="shrink-0 rounded-full border border-line-soft bg-rail px-1.5 py-px text-2xs text-text-soft">
                            <span aria-hidden="true">{email.threadMessageCount}</span>

                            <span className="sr-only">
                                {translate('list.threadMessages', {
                                    count: String(email.threadMessageCount),
                                })}
                            </span>
                        </span>
                    ) : null}

                    {/* What MailFathom read from this message, opened from the row the design project draws it on and
                        offered only where there is a reading to open. It is a mark rather than a control, and that is
                        an accessibility obligation rather than a shortcut: a row is an `option` of a listbox and holds
                        no focusable descendant, so a button here would take the keyboard path off the list. The
                        announced path to the same surface is the row's own menu, which a pointer, a finger and a
                        keyboard each reach — this is the pointer's shortcut to it and is hidden from everything that
                        would otherwise announce a second, unreachable copy of it. */}
                    {onReadings === undefined ? null : (
                        <span
                            aria-hidden="true"
                            title={translate('list.readings')}
                            className="flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-lg text-muted hover:bg-accent-soft hover:text-accent-deep pointer-coarse:size-8"
                            onPointerDown={(event) => {
                                event.stopPropagation();
                            }}
                            onPointerUp={(event) => {
                                event.stopPropagation();
                            }}
                            onClick={(event) => {
                                event.stopPropagation();
                                onReadings();
                            }}
                        >
                            <Icon name="auto_awesome" className="size-4 pointer-coarse:size-4.75" />
                        </span>
                    )}

                    <ReceivedAt at={email.receivedAt} />
                </div>

                <div className="truncate text-md">{email.subject ?? translate('list.noSubject')}</div>

                {/* The reserved line. Hidden from the accessibility tree where it holds nothing, so a row with nothing
                    to say about itself is not announced as one with an empty line in it. */}
                <div
                    aria-hidden={said === undefined ? 'true' : undefined}
                    className="h-4 overflow-hidden text-xs text-muted"
                >
                    {said}
                </div>
            </div>
        </li>
    );
}

/** Who the row is about: the sender, else the address it came from, else who it was written to. */
function correspondent(email: MailTimelineEntry): string | undefined {
    return email.senderDisplayName ?? email.senderAddress ?? email.toAddresses[0];
}
