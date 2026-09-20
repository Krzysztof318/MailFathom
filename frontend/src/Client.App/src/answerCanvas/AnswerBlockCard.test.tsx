// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from './AnswerBlockCard';
import type { AnswerBlockState } from './answerBlocks';

const label = 'Fact table';

const body = 'Two rows and a citation on each';

function renderCard(state: AnswerBlockState, onRetry?: () => void, note?: string) {
    return render(
        <LocalizationProvider>
            <AnswerBlockCard label={label} meta="2 rows" note={note} state={state} onRetry={onRetry}>
                <p>{body}</p>
            </AnswerBlockCard>
        </LocalizationProvider>,
    );
}

describe('AnswerBlockCard', () => {
    it('names the block it draws, whichever state it is in', () => {
        renderCard('empty');

        expect(screen.getByRole('article', { name: label })).toBeDefined();
    });

    it('takes focus, so the keyboard moves from one block to the next', () => {
        renderCard('ready');

        expect(screen.getByRole('article', { name: label }).tabIndex).toBe(0);
    });

    it('says what the block holds beside its name', () => {
        renderCard('ready');

        expect(screen.getByText('2 rows')).toBeDefined();
    });

    it.each([
        ['ready', true],
        ['partial', true],
        ['loading', false],
        ['empty', false],
        ['error', false],
        ['offline', false],
    ] as const)('draws the body for %s: %s', (state, drawn) => {
        renderCard(state);

        expect(screen.queryByText(body) === null).toBe(!drawn);
    });

    it('says it is waiting rather than standing there in shapes alone', () => {
        renderCard('loading');

        expect(screen.getByRole('status').textContent).toBe(`${label} is still being composed.`);
    });

    it('says which part of a partial block has not arrived, under what did', () => {
        renderCard('partial');

        expect(screen.getByText('Part of the source data is still being read.')).toBeDefined();
    });

    it('says an empty block is empty rather than drawing nothing', () => {
        renderCard('empty');

        expect(screen.getByText('There was nothing to put in this block.')).toBeDefined();
    });

    it('says a block could not be built', () => {
        renderCard('error');

        expect(screen.getByText('Could not build this block.')).toBeDefined();
    });

    it('tells an unreachable deployment apart from an empty answer', () => {
        renderCard('offline');

        expect(screen.getByText('No connection to the server — this block cannot be loaded.')).toBeDefined();
    });

    it('says what the caller knows about the failure in place of the sentence the state carries', () => {
        renderCard('offline', undefined, 'The session ended while this answer was being read.');

        expect(screen.getByText('The session ended while this answer was being read.')).toBeDefined();
        expect(screen.queryByText('No connection to the server — this block cannot be loaded.')).toBeNull();
    });

    it('offers the way out of a failure', () => {
        const retry = vi.fn();
        renderCard('error', retry);

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(retry).toHaveBeenCalledTimes(1);
    });

    it('offers none where the surface has none to offer', () => {
        renderCard('error');

        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });
});

describe('UnrecognisedAnswerBlock', () => {
    function renderUnrecognised(named: string) {
        return render(
            <LocalizationProvider>
                <UnrecognisedAnswerBlock named={named} />
            </LocalizationProvider>,
        );
    }

    it('names the type it could not draw rather than dropping the block', () => {
        renderUnrecognised('RiskScore');

        expect(
            screen.getByText(
                'This client does not recognise the block type “RiskScore”. The block was skipped — the rest of the result rendered normally.',
            ),
        ).toBeDefined();
    });

    it('says the type beside the block, so the name is readable without the sentence', () => {
        renderUnrecognised('RiskScore');

        expect(screen.getByText('type: RiskScore')).toBeDefined();
    });

    it('says what would let this client draw it', () => {
        renderUnrecognised('RiskScore');

        expect(screen.getByText('Updating the app may unlock this block type.')).toBeDefined();
    });

    it('is a block a reader lands on like any other', () => {
        renderUnrecognised('RiskScore');

        expect(screen.getByRole('article', { name: 'Unknown block' }).tabIndex).toBe(0);
    });
});
