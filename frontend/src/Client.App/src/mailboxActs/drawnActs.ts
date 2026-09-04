// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import type { ActRefusal } from './mailboxDestinations';
import type { ActedMessage, MailboxAct, MailboxActs } from './useMailboxActs';

// What the five acts are called and what they are drawn as, for every surface that offers one. It is here rather than
// beside the controls that draw them because a third surface now does — the toolbar, the selection bar, and a row's own
// menu — and a name or a symbol written twice is how *archive* comes to be two different-looking things.
//
// The two orders below are both the design project's, and they differ on purpose rather than by oversight: a strip
// reads left to right and puts what destroys beside the act it is nearest to, while a menu reads down a column and
// leaves what destroys until last, apart from everything reversible above it.

/** What each act is called and what it is drawn as, which is the design project's own symbol for it. */
export const actsDrawn: Readonly<Record<MailboxAct, { readonly icon: IconName; readonly label: MessageKey }>> = {
    archive: { icon: 'archive', label: 'mail.archive' },
    delete: { icon: 'delete', label: 'mail.delete' },
    flag: { icon: 'flag', label: 'mail.flag' },
    markUnread: { icon: 'mark_email_unread', label: 'mail.markUnread' },
    move: { icon: 'drive_file_move', label: 'mail.move' },
};

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
    return messages.length > 0 && messages.every((message) => acts.asked.get(message.storedEmailId) === act);
}
