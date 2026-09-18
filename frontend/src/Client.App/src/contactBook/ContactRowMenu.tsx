// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Contact } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';

// What a contact's row offers, which is this list's own items handed to the one menu seven lists share: where the menu
// stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s.
//
// **An item the client cannot yet perform is left out rather than drawn inert**, which is the rule
// `messageRows/MessageRowMenu.tsx` states and the reason two of the design project's five items are absent here: *write
// a message* is absent where the credential may not write a draft, and *propose a meeting* is absent altogether,
// because the Calendar space is a placeholder and a menu item that opens nothing is worse than no item at all. It
// arrives with that screen rather than being drawn ahead of it.
//
// **Nothing here performs the erasure.** The question it stands behind outlives the menu — the menu is gone the moment
// an item is chosen — so what this does is ask the screen to raise it.

export function ContactRowMenu({
    contact,
    at,
    erasable,
    onSelect,
    onOpen,
    onAskErasure,
    onClose,
}: {
    readonly contact: Contact;
    readonly at: MenuPoint;

    /** Whether this credential may take somebody out of the book at all. */
    readonly erasable: boolean;

    /** Puts this row into the selection the list holds, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    readonly onOpen: () => void;

    /** Raises the question the erasure stands behind, over this one person. */
    readonly onAskErasure: () => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const composing = useComposing();

    const items: ContextMenuItem[] = [
        { icon: 'check_box', label: translate('people.selectContacts'), choose: onSelect },
    ];

    if (composing.offered) {
        items.push({
            icon: 'edit',
            label: translate('people.writeMessage'),
            choose: () => {
                composing.compose({ kind: 'new', to: [contact.preferredAddress] });
            },
        });
    }

    items.push({ icon: 'person', label: translate('people.openContact'), choose: onOpen });

    if (erasable) {
        items.push({
            icon: 'delete',
            label: translate('people.deleteContact'),
            destroys: true,
            choose: onAskErasure,
        });
    }

    return <ContextMenu header={contact.displayName} at={at} items={items} onClose={onClose} />;
}
