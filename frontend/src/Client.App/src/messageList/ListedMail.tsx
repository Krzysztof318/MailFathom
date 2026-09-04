// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState, type ReactNode } from 'react';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';
import { ListedMailContext, mostPlacesRemembered, type ListedMail, type ListedMailbox } from './useListedMail';

// What the list has drawn, held for the surfaces above it. What it holds is refs on purpose: nothing in them changes
// what is on the screen, and re-rendering on every arriving page would cost the list its scrolling — the one
// interaction this screen exists to keep smooth. The value carrying them is state built once so its identity never
// changes either, which is what keeps a consumer from re-rendering because this provider did.

export function ListedMailProvider({ children }: { readonly children: ReactNode }) {
    const places = useRef(new Map<string, ActedMessage>());
    const mailbox = useRef<ListedMailbox | null>(null);

    // Built once, so the value never changes identity and no consumer re-renders because of this provider.
    const [listed] = useState<ListedMail>(() => ({
        placeOf: (storedEmailId) => places.current.get(storedEmailId) ?? null,
        drew: (emails) => {
            for (const email of emails) {
                places.current.set(email.id, {
                    storedEmailId: email.id,
                    account: email.account,
                    folder: email.folder,
                });
            }

            // A Map keeps what was put in it in that order, so the oldest reading is what a full map gives up.
            for (const oldest of places.current.keys()) {
                if (places.current.size <= mostPlacesRemembered) {
                    break;
                }

                places.current.delete(oldest);
            }
        },
        selectAll: () => {
            mailbox.current?.selectAll();
        },
        takeFocus: () => {
            mailbox.current?.takeFocus();
        },
        listing: (list) => {
            mailbox.current = list;
        },
    }));

    return <ListedMailContext value={listed}>{children}</ListedMailContext>;
}
