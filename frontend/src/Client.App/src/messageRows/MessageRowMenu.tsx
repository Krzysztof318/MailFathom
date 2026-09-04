// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { useComposing, type Composing } from '../composer/useComposing';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { actsDrawn, actsInARowMenu, underway } from '../mailboxActs/drawnActs';
import { useMailboxActs, type ActedMessage, type MailboxActs } from '../mailboxActs/useMailboxActs';
import type { Translate } from '../localization/useLocalization';
import { useLocalization } from '../localization/useLocalization';

// What a message row offers, which is the design project's own menu for it: picking messages out, answering the
// message, and the five acts that change the mailbox it is in. It is this row's items and nothing else — where the
// menu stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s, because six more lists draw
// the same menu with items of their own.
//
// **Nothing here performs an act.** Every item routes through the same calls the toolbar presses and reports through
// the same toast surface, so filing a message from a row and filing it from the strip above are one act asked for two
// ways rather than two implementations that will come to disagree.
//
// **An item the client cannot yet perform is left out rather than drawn inert.** That is the opposite of what a strip
// does, and deliberately: a strip is a fixed row whose controls would otherwise appear and disappear under a reader's
// cursor, while a menu is read top to bottom in the moment it opens, and a column of sentences nobody can act on is
// not a menu. *Ask the agent*, which the design draws second, is out for the same reason and arrives with the intent
// field.
//
// **The two acts that stand behind a question are asked outside this menu**, because the question outlives it: the
// menu is gone the moment an item is chosen, and a dialog inside it would go with it.

/** The two acts a question stands in front of, which are raised by the surface the menu was opened from. */
export type ActAsked = 'delete' | 'move';

export function MessageRowMenu({
    email,
    messages,
    at,
    onSelect,
    onAsk,
    onClose,
}: {
    readonly email: MailTimelineEntry;

    /** Where this row's message belongs, which every act has to name. Empty for a row the client cannot place. */
    readonly messages: readonly ActedMessage[];

    readonly at: MenuPoint;

    /** Puts this row into the selection the list already models, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    /** Raises the question an act stands behind, over the messages it is about. */
    readonly onAsk: (act: ActAsked, messages: readonly ActedMessage[]) => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const composing = useComposing();

    return (
        <ContextMenu
            header={email.subject ?? translate('list.noSubject')}
            at={at}
            items={rowItems({ email, messages, acts, composing, translate, onSelect, onAsk })}
            onClose={onClose}
        />
    );
}

/** Everything this row offers, in the order the design project draws it. */
function rowItems({
    email,
    messages,
    acts,
    composing,
    translate,
    onSelect,
    onAsk,
}: {
    readonly email: MailTimelineEntry;
    readonly messages: readonly ActedMessage[];
    readonly acts: MailboxActs;
    readonly composing: Composing;
    readonly translate: Translate;
    readonly onSelect: () => void;
    readonly onAsk: (act: ActAsked, messages: readonly ActedMessage[]) => void;
}): readonly ContextMenuItem[] {
    const answering: readonly ContextMenuItem[] = composing.offered
        ? [
              {
                  icon: 'reply',
                  label: translate('mail.reply'),
                  choose: () => {
                      composing.compose({ kind: 'answer', answers: 'senderOnly', storedEmailId: email.id });
                  },
              },
              {
                  icon: 'forward',
                  label: translate('mail.forward'),
                  choose: () => {
                      composing.compose({ kind: 'answer', answers: 'forward', storedEmailId: email.id });
                  },
              },
          ]
        : [];

    const mailbox = actsInARowMenu
        .filter((act) => acts.refusalOf(act, messages) === null && !underway(acts, act, messages))
        .map((act) => ({
            icon: actsDrawn[act].icon,
            label: translate(actsDrawn[act].label),
            destroys: act === 'delete',
            choose: () => {
                if (act === 'delete' || act === 'move') {
                    onAsk(act, messages);
                } else {
                    acts.perform(act, messages);
                }
            },
        }));

    return [{ icon: 'check_box', label: translate('mail.selectMessages'), choose: onSelect }, ...answering, ...mailbox];
}
