// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type ActedMessage, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { AgentHandOverContext, type AgentHandOver } from '../routing/agentHandOver';
import { HeadActs } from './HeadActs';

const message: ActedMessage = {
    storedEmailId: '00000000-0000-4000-8000-000000000000',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
};

const conversation: AgentHandOver = {
    scope: { kind: 'thread', subject: '0198f4a1-0000-7000-8000-00000000b001' },
    title: 'Hall lease',
};

// jsdom answers every media query `false`, which is the phone, so a test about the pill states a width above it.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

/** The window is wider than a phone and no wider than one pane, which is where the head is compact and the pill stays. */
function aOnePaneWindowWiderThanAPhone(): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('43.75rem'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function drawHead({
    offered = true,
    acting = message,
    acts = actsOffering(),
    compact = false,
    handToAgent = null,
}: {
    readonly offered?: boolean;
    readonly acting?: ActedMessage | null;
    readonly acts?: MailboxActs;
    readonly compact?: boolean;
    readonly handToAgent?: ((handOver: AgentHandOver) => void) | null;
} = {}): { composed: ReturnType<typeof vi.fn> } {
    const composed = vi.fn();
    const composing: Composing = { offered, drafts: false, opening: null, compose: composed, close: () => undefined };

    render(
        <LocalizationProvider>
            <AgentHandOverContext value={handToAgent}>
                <MailboxActsContext value={acts}>
                    <ComposingContext value={composing}>
                        <HeadActs compact={compact} message={acting} thread={conversation} />
                    </ComposingContext>
                </MailboxActsContext>
            </AgentHandOverContext>
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

    // A flag already on its way is what the next press is read against, exactly as the read control reads one: the
    // control turns round at once rather than saying the first press is still travelling, so pressing it twice gives
    // the two directions rather than the same act submitted again.
    it('turns the flag round where one is already on its way, so the next press takes it off', () => {
        const performed = vi.fn();
        drawHead({
            acts: {
                ...actsOffering(performed),
                asked: new Map([
                    [message.storedEmailId, { act: 'flag', from: message.folder, leaves: false, destroys: false }],
                ]),
            },
        });

        expect(screen.queryByRole('button', { name: 'Flag' })).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Unflag' }));

        expect(performed).toHaveBeenCalledWith('unflag', [message]);
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

    it('hands the conversation it stands over to the agent', () => {
        aOnePaneWindowWiderThanAPhone();
        const handToAgent = vi.fn();
        drawHead({ handToAgent });

        fireEvent.click(screen.getByRole('button', { name: 'Ask' }));

        expect(handToAgent).toHaveBeenCalledWith(conversation);
    });

    it('draws no way to the agent for a credential that has no agent to reach', () => {
        aOnePaneWindowWiderThanAPhone();
        drawHead();

        expect(screen.queryByRole('button', { name: 'Ask' })).toBeNull();
    });

    // The compact bar and the phone are two widths apart: a one-pane window wider than a phone draws the acts as symbols
    // and keeps the pill, because nothing else there would carry the conversation to the agent.
    it('keeps the agent in the compact head of a one-pane window wider than a phone', () => {
        aOnePaneWindowWiderThanAPhone();
        const handToAgent = vi.fn();
        drawHead({ compact: true, handToAgent });

        fireEvent.click(screen.getByRole('button', { name: 'Ask' }));

        expect(handToAgent).toHaveBeenCalledWith(conversation);
    });

    it('keeps the agent out of the head on a phone, where the design moves it into the thread state', () => {
        drawHead({ compact: true, handToAgent: vi.fn() });

        expect(screen.queryByRole('button', { name: /Ask/u })).toBeNull();
        expect(screen.getByRole('button', { name: 'Reply' })).toBeDefined();
    });
});
