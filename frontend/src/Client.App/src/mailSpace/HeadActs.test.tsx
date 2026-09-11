// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { HeadActs, type HeadMessage } from './HeadActs';

const message: HeadMessage = {
    storedEmailId: '00000000-0000-4000-8000-000000000000',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
};

function drawHead({
    offered = true,
    acting = message,
    acts = actsOffering(),
    compact = false,
}: {
    readonly offered?: boolean;
    readonly acting?: HeadMessage | null;
    readonly acts?: MailboxActs;
    readonly compact?: boolean;
} = {}): { composed: ReturnType<typeof vi.fn> } {
    const composed = vi.fn();
    const composing: Composing = { offered, opening: null, compose: composed, close: () => undefined };

    render(
        <LocalizationProvider>
            <MailboxActsContext value={acts}>
                <ComposingContext value={composing}>
                    <HeadActs compact={compact} message={acting} />
                </ComposingContext>
            </MailboxActsContext>
        </LocalizationProvider>,
    );

    return { composed };
}

function actsOffering(performed: MailboxActs['perform'] = () => undefined): MailboxActs {
    return { ...nothingActed, refusalOf: () => null, perform: performed };
}

describe('HeadActs', () => {
    it.each([
        ['Reply', 'senderOnly'],
        ['Forward', 'forward'],
    ])('opens the composer on %s, carrying the message it was pressed on', (name, answers) => {
        const { composed } = drawHead();

        fireEvent.click(screen.getByRole('button', { name }));

        expect(composed).toHaveBeenCalledWith({
            kind: 'answer',
            answers,
            storedEmailId: message.storedEmailId,
        });
    });

    it('flags the message being read', () => {
        const performed = vi.fn();
        drawHead({ acts: actsOffering(performed) });

        fireEvent.click(screen.getByRole('button', { name: 'Flag' }));

        expect(performed).toHaveBeenCalledWith('flag', [message]);
    });

    it('offers a flagged message the act that takes the flag off instead', () => {
        const performed = vi.fn();
        const flagged = { ...message, flagged: true };
        drawHead({ acting: flagged, acts: actsOffering(performed) });

        expect(screen.queryByRole('button', { name: 'Flag' })).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Unflag' }));

        expect(performed).toHaveBeenCalledWith('unflag', [flagged]);
    });

    it('says why the flag cannot be changed rather than answering nothing when it is pressed', () => {
        const performed = vi.fn();
        drawHead({ acts: { ...nothingActed, perform: performed } });

        const refused = screen.getByRole('button', {
            name: 'Flag — this credential may not change mail on your mail server.',
        });

        fireEvent.click(refused);

        expect(refused).toHaveProperty('ariaDisabled', 'true');
        expect(performed).not.toHaveBeenCalled();
    });

    it('says an act already on its way is on its way rather than submitting it twice', () => {
        const performed = vi.fn();
        drawHead({
            acts: {
                ...actsOffering(performed),
                asked: new Map([[message.storedEmailId, { act: 'flag', from: message.folder, leaves: false }]]),
            },
        });

        fireEvent.click(screen.getByRole('button', { name: 'Flag — this is already on its way to your mail server.' }));

        expect(performed).not.toHaveBeenCalled();
    });

    it('draws the three as what they are where the deployment refuses writing at all', () => {
        const { composed } = drawHead({ offered: false, acts: nothingActed });

        fireEvent.click(screen.getByRole('button', { name: 'Reply — not built yet' }));
        fireEvent.click(screen.getByRole('button', { name: 'Forward — not built yet' }));

        expect(composed).not.toHaveBeenCalled();
    });

    it('draws them as what they are in a head that is about no one message', () => {
        const performed = vi.fn();
        const { composed } = drawHead({ acting: null, acts: actsOffering(performed) });

        fireEvent.click(screen.getByRole('button', { name: 'Reply — not built yet' }));
        fireEvent.click(
            screen.getByRole('button', {
                name: 'Flag — nothing is open or selected for this to be about.',
            }),
        );

        expect(composed).not.toHaveBeenCalled();
        expect(performed).not.toHaveBeenCalled();
    });

    it('keeps the agent out of the compact head, where the design draws the symbols alone', () => {
        drawHead({ compact: true });

        expect(screen.queryByRole('button', { name: /Ask/u })).toBeNull();
        expect(screen.getByRole('button', { name: 'Reply' })).toBeDefined();
    });
});
