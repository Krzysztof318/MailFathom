// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ListedMailProvider } from './ListedMail';
import { mostPlacesRemembered, useListedMail, type ListedMail } from './useListedMail';

function drawn(at: number): { readonly id: string; readonly account: string; readonly folder: string } {
    return { id: `message-${String(at)}`, account: 'work', folder: 'work-inbox' };
}

function held(): ListedMail {
    function Surrounded({ children }: { readonly children: ReactNode }) {
        return <ListedMailProvider>{children}</ListedMailProvider>;
    }

    return renderHook(() => useListedMail(), { wrapper: Surrounded }).result.current;
}

describe('ListedMailProvider', () => {
    it('says where a message the list drew belongs, which the workspace’s identities alone cannot', () => {
        const listed = held();

        listed.drew([drawn(1)]);

        expect(listed.placeOf('message-1')).toStrictEqual({
            storedEmailId: 'message-1',
            account: 'work',
            folder: 'work-inbox',
        });
    });

    it('says nothing about a message no list has drawn, rather than guessing an account for it', () => {
        expect(held().placeOf('message-nobody-drew')).toBeNull();
    });

    it('gives up the oldest reading once it is holding as much as it may, a folder outgrowing any map', () => {
        const listed = held();

        listed.drew(Array.from({ length: mostPlacesRemembered + 2 }, (_, at) => drawn(at)));

        expect(listed.placeOf('message-0')).toBeNull();
        expect(listed.placeOf('message-1')).toBeNull();
        expect(listed.placeOf(`message-${String(mostPlacesRemembered + 1)}`)).not.toBeNull();
    });

    it('takes the listing in at once through the list that registered it, and does nothing once it has left', () => {
        const listed = held();
        const everything = vi.fn();

        listed.listing(everything);
        listed.selectAll();

        listed.listing(null);
        listed.selectAll();

        expect(everything).toHaveBeenCalledOnce();
    });
});
