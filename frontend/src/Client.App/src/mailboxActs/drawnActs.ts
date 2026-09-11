// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import type { ActRefusal } from './mailboxDestinations';
import type { ActedMessage, MailboxAct, MailboxActs } from './useMailboxActs';

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
    markUnread: 'menu.markUnread',
    move: 'menu.move',
};

// Taking a flag off is in neither order below, and that is the difference between a strip and a head rather than an
// omission. A strip and a row's menu stand over whatever is picked out — one message or two hundred, flagged and
// unflagged among them — so which direction a single control would go in is a question they cannot answer; the head of
// the message being read is about exactly one message whose flag the screen is already holding, which is why the design
// draws the toggle there and *Flaga* here.

/** The order a strip of controls draws them in: the toolbar, and the bar that stands over a selection. */
export const actsOnAStrip: readonly MailboxAct[] = ['archive', 'delete', 'flag', 'markUnread', 'move'];

/** The order a row's own menu draws them in, with the one that cannot be taken back last. */
export const actsInARowMenu: readonly MailboxAct[] = ['archive', 'flag', 'markUnread', 'move', 'delete'];

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
