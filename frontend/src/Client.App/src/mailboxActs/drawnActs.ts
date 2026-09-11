// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { drawnUnread, type ReadMarking } from '../readMarking/useReadMarking';
import type { ActRefusal } from './mailboxDestinations';
import type { ActedMessage, FlagAct, MailboxAct, MailboxActs } from './useMailboxActs';

// What the acts are called and what they are drawn as, for every surface that offers one. It is here rather than
// beside the controls that draw them because a fourth surface now does — the toolbar, the selection bar, a row's own
// menu, and the head of the message being read — and a name or a symbol written twice is how *archive* comes to be two
// different-looking things.
//
// The two orders below are both the design project's, and they differ on purpose rather than by oversight: a strip
// reads left to right and puts what destroys beside the act it is nearest to, while a menu reads down a column and
// leaves what destroys until last, apart from everything reversible above it.

/** What each act is called and what it is drawn as, which is the design project's own symbol for it. */
export const actsDrawn: Readonly<Record<MailboxAct, { readonly icon: IconName; readonly label: MessageKey }>> = {
    archive: { icon: 'archive', label: 'mail.archive' },
    delete: { icon: 'delete', label: 'mail.delete' },
    flag: { icon: 'flag', label: 'mail.flag' },
    unflag: { icon: 'flag', label: 'mail.unflag' },
    markRead: { icon: 'mark_email_read', label: 'mail.markRead' },
    markUnread: { icon: 'mark_email_unread', label: 'mail.markUnread' },
    move: { icon: 'drive_file_move', label: 'mail.move' },
};

/**
 * What a row's menu calls each act, where that differs from the strip: a menu item is read as a sentence about the
 * message under the pointer, and the design words three of them that way.
 */
export const actsSaidInAMenu: Readonly<Record<MailboxAct, MessageKey>> = {
    archive: 'mail.archive',
    delete: 'mail.delete',
    flag: 'menu.flag',
    unflag: 'menu.unflag',
    markRead: 'menu.markRead',
    markUnread: 'menu.markUnread',
    move: 'menu.move',
};

// **Two slots in the orders below go both ways, and a rule decides each of them rather than a single message.** The
// design draws both as toggles — a row's context menu says *Remove flag* over a flagged message and the toolbar says
// the same — so a strip standing over two hundred messages answers the direction the way it answers the read one: from
// what the messages under it are drawn as, which it holds for every one of them. `readActFor` and `flagActFor` are
// those two rules, and the orders name each slot by the act it reads as most of the time.

/** The order a strip of controls draws them in: the toolbar, and the bar that stands over a selection. */
export const actsOnAStrip: readonly MailboxAct[] = ['archive', 'delete', 'flag', 'markUnread', 'move'];

/** The order a row's own menu draws them in, with the one that cannot be taken back last. */
export const actsInARowMenu: readonly MailboxAct[] = ['archive', 'flag', 'markUnread', 'move', 'delete'];

/**
 * Which way the read control goes for these messages, which is the act the `markUnread` slot above actually offers.
 *
 * **Marking read is the default, and one shared state turns it round.** Every message drawn read is offered the act
 * that marks them unread; anything else — every message unread, or a mixture of the two — is offered the act that
 * marks them read. Read against what the reader is looking at rather than against what the deployment last answered,
 * because those differ for minutes at a time: a message whose body has just been drawn is already marked read by this
 * client, and a control offering to mark it read again would be offering to do what has been done.
 *
 * A pending act of its own wins over both, which is what makes pressing the control twice in a row give a reader the
 * two directions rather than the same one: the second press reads the first press's state.
 *
 * An empty list is offered the same act as messages that are all read. It is refused before it can be pressed —
 * `nothingToActOn` — so what this decides there is only which name the control wears while it says so, and the slot
 * keeping the name the order above gives it is what stops a strip's wording from changing as a selection is cleared.
 */
export function readActFor(acts: MailboxActs, marking: ReadMarking, messages: readonly ActedMessage[]): FlagAct {
    function unreadNow(message: ActedMessage): boolean {
        const asked = acts.asked.get(message.storedEmailId);

        if (asked?.act === 'markUnread') {
            return true;
        }

        if (asked?.act === 'markRead') {
            return false;
        }

        return drawnUnread(marking, message.storedEmailId, message.unread);
    }

    return messages.every((message) => !unreadNow(message)) ? 'markUnread' : 'markRead';
}

/**
 * Which way the flag control goes for these messages, which is the act the `flag` slot above actually offers.
 *
 * **Putting a flag on is the default, and one shared state turns it round**, exactly as the read control's rule
 * reads: every message drawn flagged is offered the act that takes the flag off, and anything else — none of them
 * flagged, or a mixture — is offered the act that puts one on. Read against what the reader is looking at rather than
 * against what the deployment last answered, because a mailbox mutation converges minutes after it is written down
 * and a control offering to flag a row already drawn flagged would be offering to do what has been done.
 *
 * A pending act of its own wins over the observation, which is what makes pressing the control twice give a reader
 * the two directions rather than the same one.
 *
 * An empty list is offered the act that puts a flag on, for the reason an empty list is offered `markUnread`: it is
 * refused before it can be pressed, so what this decides there is only the name the control wears while it says so.
 */
export function flagActFor(acts: MailboxActs, messages: readonly ActedMessage[]): FlagAct {
    function flaggedNow(message: ActedMessage): boolean {
        const asked = acts.asked.get(message.storedEmailId);

        if (asked?.act === 'flag') {
            return true;
        }

        if (asked?.act === 'unflag') {
            return false;
        }

        return message.flagged;
    }

    return messages.length > 0 && messages.every(flaggedNow) ? 'unflag' : 'flag';
}

/** Why a control cannot act, exhaustive by its own type so a reason added later has to be given words. */
export const refusalSaid: Readonly<Record<ActRefusal, MessageKey>> = {
    notOffered: 'act.notOffered',
    nothingToActOn: 'act.nothingToActOn',
    noArchiveFolder: 'act.noArchiveFolder',
    noTrashFolder: 'act.noTrashFolder',
    severalAccounts: 'act.severalAccounts',
    noOtherFolder: 'act.noOtherFolder',
    foldersUnknown: 'act.foldersUnknown',
};

/** Whether this act is already being carried out for every message the control is about. */
export function underway(acts: MailboxActs, act: MailboxAct, messages: readonly ActedMessage[]): boolean {
    return messages.length > 0 && messages.every((message) => acts.asked.get(message.storedEmailId)?.act === act);
}

/**
 * Why a control for this act cannot be pressed, or `null` where it can — the two questions every surface asks in order.
 *
 * One function rather than the same pair of calls written at each surface, because the order between them is the
 * decision: an act the deployment refuses is refused whether or not one is already on its way, so the refusal is read
 * first and *already underway* is what is left over. A surface that asked them the other way round would tell somebody
 * their archive is on its way to an account that names no archive folder.
 */
export function standsInTheWay(
    acts: MailboxActs,
    act: MailboxAct,
    messages: readonly ActedMessage[],
): ActRefusal | 'underway' | null {
    const refusal = acts.refusalOf(act, messages);

    if (refusal !== null) {
        return refusal;
    }

    return underway(acts, act, messages) ? 'underway' : null;
}
