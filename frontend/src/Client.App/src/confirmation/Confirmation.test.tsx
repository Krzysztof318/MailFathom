// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { storeLocale } from '../localization/locale';
import { Confirmation, type Reversal, type WayOut } from './Confirmation';

const asking = 'Ask';
const question = 'Move 4 messages to Archive?';
const consequence = 'They leave Inbox on the work account.';

function Asking({
    reversal,
    ways,
    cautions,
}: {
    readonly reversal: Reversal;
    readonly ways: readonly WayOut[];
    readonly cautions: readonly string[];
}) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {asking}
            </button>

            <Confirmation
                asked={asked}
                mark="archive"
                question={question}
                consequence={<p>{consequence}</p>}
                cautions={cautions}
                reversal={reversal}
                ways={ways}
            />
        </>
    );
}

const permanent: Reversal = { kind: 'permanent', said: 'The messages are not recoverable afterwards.' };

function drawConfirmation(
    reversal: Reversal = permanent,
    cautions: readonly string[] = [],
): {
    acted: ReturnType<typeof vi.fn>;
    leftAside: ReturnType<typeof vi.fn>;
    keptAsTheyWere: ReturnType<typeof vi.fn>;
} {
    const acted = vi.fn();
    const leftAside = vi.fn();

    // The first way out carries a `run` although leaving usually does not, because it is the only way to tell apart the
    // answer that is no answer: without it, a dialog closed by Escape resolving to the first way out would find nothing
    // to run and look exactly like one that resolved to nothing at all.
    const keptAsTheyWere = vi.fn();

    render(
        <LocalizationProvider>
            <Asking
                reversal={reversal}
                cautions={cautions}
                ways={[
                    { said: 'Keep them where they are', manner: 'back', run: keptAsTheyWere },
                    { said: 'Leave a copy', manner: 'aside', run: leftAside },
                    { said: 'Move them', manner: 'act', run: acted },
                ]}
            />
        </LocalizationProvider>,
    );

    return { acted, leftAside, keptAsTheyWere };
}

function ask(): void {
    fireEvent.click(screen.getByRole('button', { name: asking }));
}

function press(said: string): void {
    fireEvent.click(screen.getByRole('button', { name: said }));
}

// Leaving the question rather than answering it, which is what Escape and the platform's own close both are.
//
// The event is dispatched rather than left to `close()`, because jsdom queues that one and nothing in a synchronous
// test drains the queue — so a `close()` alone reports nothing and every assertion below it would hold whatever the
// component did. A press arrives here through `fireEvent` and needs no such help.
function leave(): void {
    const dialog = screen.getByRole<HTMLDialogElement>('dialog');

    dialog.close();
    fireEvent(dialog, new Event('close'));
}

describe('Confirmation', () => {
    it('does nothing on being opened, which is the whole of what a confirmation is for', () => {
        const { acted, leftAside } = drawConfirmation();

        ask();

        expect(acted).not.toHaveBeenCalled();
        expect(leftAside).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog').textContent).toContain(question);
    });

    it('states what will change in the words the screen asking gave it', () => {
        drawConfirmation();
        ask();

        expect(screen.getByRole('dialog').textContent).toContain(consequence);
    });

    it('performs the act the way out named, and only that one', () => {
        const { acted, leftAside } = drawConfirmation();

        ask();
        press('Move them');

        expect(acted).toHaveBeenCalledTimes(1);
        expect(leftAside).not.toHaveBeenCalled();
    });

    it('performs the second way out where that is the one pressed', () => {
        const { acted, leftAside } = drawConfirmation();

        ask();
        press('Leave a copy');

        expect(leftAside).toHaveBeenCalledTimes(1);
        expect(acted).not.toHaveBeenCalled();
    });

    it('performs nothing where the question was left rather than answered, the first way out included', () => {
        const { acted, leftAside, keptAsTheyWere } = drawConfirmation();

        ask();
        leave();

        expect(acted).not.toHaveBeenCalled();
        expect(leftAside).not.toHaveBeenCalled();
        expect(keptAsTheyWere).not.toHaveBeenCalled();
    });

    it('performs nothing on being left after an answer, rather than repeating that answer', () => {
        const { acted } = drawConfirmation();

        ask();
        press('Move them');
        ask();
        leave();

        expect(acted).toHaveBeenCalledTimes(1);
    });

    it.each([
        [1, 'You can take this back for 1 second afterwards.'],
        [2, 'You can take this back for 2 seconds afterwards.'],
        [10, 'You can take this back for 10 seconds afterwards.'],
    ])(
        'says how long a reversible act can be taken back in, in the form English takes at that number',
        (forSeconds, said) => {
            drawConfirmation({ kind: 'undoable', forSeconds });
            ask();

            expect(screen.getByRole('dialog').textContent).toContain(said);
        },
    );

    // Polish is where the period actually has to be read: it takes one form at one, a second at two through four, and a
    // third above that, and a screen that interpolated a number into one sentence would be wrong at two of the three.
    it.each([
        [1, 'Możesz to cofnąć jeszcze przez 1 sekundę.'],
        [2, 'Możesz to cofnąć jeszcze przez 2 sekundy.'],
        [10, 'Możesz to cofnąć jeszcze przez 10 sekund.'],
    ])('says how long it can be taken back in the form Polish takes at that number', (forSeconds, said) => {
        storeLocale('pl');

        drawConfirmation({ kind: 'undoable', forSeconds });
        ask();

        expect(screen.getByRole('dialog').textContent).toContain(said);
    });

    it('says what an irreversible act costs, in the words of the act rather than in its own', () => {
        drawConfirmation();
        ask();

        expect(screen.getByRole('dialog').textContent).toContain('The messages are not recoverable afterwards.');
    });

    it('says an act can be taken back without naming a period where the deployment decides it', () => {
        drawConfirmation({ kind: 'recallable', said: 'You can take it back until it has gone out.' });
        ask();

        const asked = screen.getByRole('dialog').textContent;

        expect(asked).toContain('You can take it back until it has gone out.');
        expect(asked).not.toContain('take this back for');
    });

    it('names what the act would happen without, where anything is missing', () => {
        drawConfirmation(permanent, ['Two of them are already in Archive.']);
        ask();

        expect(screen.getByRole('dialog').textContent).toContain('Two of them are already in Archive.');
    });

    it('names nothing missing where nothing is', () => {
        drawConfirmation();
        ask();

        expect(screen.queryByRole('list')).toBeNull();
    });

    it('is named by its question and described by what would change, so both are announced', () => {
        drawConfirmation();
        ask();

        const asked = screen.getByRole('dialog');
        const named = asked.getAttribute('aria-labelledby');
        const described = asked.getAttribute('aria-describedby');

        expect(named === null ? null : document.getElementById(named)?.textContent).toBe(question);
        expect(described === null ? null : document.getElementById(described)?.textContent).toContain(consequence);
    });
});
