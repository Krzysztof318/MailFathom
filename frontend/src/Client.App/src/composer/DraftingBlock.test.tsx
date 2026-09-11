// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { DraftingBlock, DraftingStanding } from './DraftingBlock';

// What the block asks for, which is the one thing a screen cannot see: the acts and the tone are a sentence composed
// here and sent as one instruction, and what somebody typed is the rest of it. The wording of each act is asserted by
// what it contains rather than word for word, so an instruction reworded for the writer does not fail a test about
// the control that sends it.

function drawBlock(asked = '', busy = false): { drafted: ReturnType<typeof vi.fn> } {
    const drafted = vi.fn();

    render(
        <LocalizationProvider>
            <DraftingBlock context="Quarterly invoice" busy={busy} asked={asked} onDraft={drafted} />
        </LocalizationProvider>,
    );

    return { drafted };
}

describe('DraftingBlock', () => {
    it('offers the four acts, the three tones, and says what the draft is written against', () => {
        drawBlock();

        for (const act of ['Write a draft', 'Shorten', 'Expand', 'Fix the language']) {
            expect(screen.getByRole('button', { name: act })).toBeDefined();
        }

        for (const tone of ['Formal', 'Neutral', 'Direct']) {
            expect(screen.getByRole('radio', { name: tone })).toBeDefined();
        }

        expect(screen.getByText('Context: Quarterly invoice')).toBeDefined();
        expect(screen.getByText('the draft stays local until you send it')).toBeDefined();
    });

    it('stands on the tone nobody chose, which is the one the design draws as in force', () => {
        drawBlock();

        expect(screen.getByRole('radio', { name: 'Neutral', checked: true })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Formal', checked: false })).toBeDefined();
    });

    it('sends what the act asks for and what was typed as one instruction', () => {
        const { drafted } = drawBlock();

        fireEvent.change(screen.getByRole('textbox', { name: 'What the draft should say' }), {
            target: { value: 'ask for a 5% cap' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Shorten' }));

        const asked = drafted.mock.calls[0]?.[0] as string;

        expect(asked).toContain('short');
        expect(asked.endsWith('ask for a 5% cap')).toBe(true);
    });

    it('carries the tone that is in force, and carries none where nobody chose one', () => {
        const { drafted } = drawBlock();

        fireEvent.click(screen.getByRole('button', { name: 'Write a draft' }));

        expect(drafted.mock.calls[0]?.[0]).not.toContain('tone');

        fireEvent.click(screen.getByRole('radio', { name: 'Formal' }));
        fireEvent.click(screen.getByRole('button', { name: 'Write a draft' }));

        expect(drafted.mock.calls[1]?.[0]).toContain('formal');
    });

    it('asks once for what the composer was opened asking for, with the words still in the field', () => {
        const { drafted } = drawBlock('accept the SLA');

        expect(drafted).toHaveBeenCalledTimes(1);
        expect(drafted.mock.calls[0]?.[0]).toContain('accept the SLA');
        expect(screen.getByRole('textbox', { name: 'What the draft should say' })).toHaveProperty(
            'value',
            'accept the SLA',
        );
    });

    it('asks for nothing on its own where the composer was opened asking for nothing', () => {
        const { drafted } = drawBlock();

        expect(drafted).not.toHaveBeenCalled();
    });

    // A radio group is announced as one control the arrows choose inside, so the keyboard has to keep that promise:
    // the tone in force is the group's only tab stop, and an arrow moves both the choice and the focus.
    it('moves the tone with the arrows and takes the focus with it, the group being one tab stop', () => {
        drawBlock();

        const neutral = screen.getByRole('radio', { name: 'Neutral' });

        expect(neutral).toHaveProperty('tabIndex', 0);
        expect(screen.getByRole('radio', { name: 'Formal' })).toHaveProperty('tabIndex', -1);

        fireEvent.keyDown(neutral, { key: 'ArrowLeft' });

        const formal = screen.getByRole('radio', { name: 'Formal', checked: true });

        expect(document.activeElement).toBe(formal);
        expect(formal).toHaveProperty('tabIndex', 0);
    });

    it('wraps at the ends rather than stopping, which is what a radio group does', () => {
        drawBlock();

        fireEvent.keyDown(screen.getByRole('radio', { name: 'Neutral' }), { key: 'ArrowRight' });
        fireEvent.keyDown(screen.getByRole('radio', { name: 'Direct' }), { key: 'ArrowRight' });

        expect(screen.getByRole('radio', { name: 'Formal', checked: true })).toBeDefined();
    });

    it('refuses a second act while one is still being written', () => {
        drawBlock('', true);

        expect(screen.getByRole('button', { name: 'Write a draft' })).toHaveProperty('disabled', true);
    });
});

describe('DraftingStanding', () => {
    it('says a draft is being written, and offers nothing to act on while it is', () => {
        render(
            <LocalizationProvider>
                <DraftingStanding busy drafted onRestore={vi.fn()} onAccept={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(screen.getByRole('status')).toHaveProperty('textContent', 'AI is drafting…');
        expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull();
    });

    it('offers the way back and the way forward once a draft stands', () => {
        const restored = vi.fn();
        const accepted = vi.fn();

        render(
            <LocalizationProvider>
                <DraftingStanding busy={false} drafted onRestore={restored} onAccept={accepted} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Restore my version' }));
        fireEvent.click(screen.getByRole('button', { name: 'Accept' }));

        expect(restored).toHaveBeenCalledOnce();
        expect(accepted).toHaveBeenCalledOnce();
    });

    it('says nothing where no draft has been written and none is being written', () => {
        render(
            <LocalizationProvider>
                <DraftingStanding busy={false} drafted={false} onRestore={vi.fn()} onAccept={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull();
    });
});
