// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { useStripFit } from './useStripFit';

// jsdom lays nothing out, so what a control needs and what a strip has are answered from attributes the test writes:
// every element is as wide as its `data-width` says, and a strip has the room its `data-room` says.
const measured = {
    offsetWidth: Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth'),
    clientWidth: Object.getOwnPropertyDescriptor(Element.prototype, 'clientWidth'),
};

beforeEach(() => {
    Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
        configurable: true,
        get(this: HTMLElement) {
            return Number(this.dataset['width'] ?? 0);
        },
    });
    Object.defineProperty(Element.prototype, 'clientWidth', {
        configurable: true,
        get(this: Element) {
            return Number(this.getAttribute('data-room') ?? 0);
        },
    });
});

afterEach(() => {
    for (const [name, descriptor] of Object.entries(measured)) {
        const owner = name === 'offsetWidth' ? HTMLElement.prototype : Element.prototype;

        if (descriptor === undefined) {
            Reflect.deleteProperty(owner, name);
        } else {
            Object.defineProperty(owner, name, descriptor);
        }
    }
});

// A strip as the toolbar draws one: the primary control and the divider after it first, while the names fit beside
// them, and the acts after. What each child needs is fixed; what the strip has is what a test varies.
const stripName = 'strip';
const primaryName = 'write';

function Strip({
    withPrimary,
    room,
    contents = 'en',
}: {
    readonly withPrimary: boolean;
    readonly room: number;
    readonly contents?: string;
}) {
    const { strip, fit } = useStripFit(withPrimary, contents);

    return (
        <div ref={strip} role="toolbar" aria-label={stripName} data-room={room} data-fit={fit}>
            {withPrimary && fit === 'labelled' ? (
                <>
                    <button type="button" data-width="100">
                        {primaryName}
                    </button>
                    <span data-width="1" />
                </>
            ) : null}

            {['a', 'b', 'c', 'd', 'e'].map((act) => (
                <button key={act} type="button" data-width="80">
                    {act}
                </button>
            ))}
        </div>
    );
}

function fitOf(): string | null {
    return screen.getByRole('toolbar', { name: stripName }).getAttribute('data-fit');
}

describe('useStripFit', () => {
    it('keeps every name and the primary control in the strip while the whole of it fits', () => {
        render(<Strip withPrimary room={1000} />);

        expect(fitOf()).toBe('labelled');
    });

    it('gives the primary control up first, keeping the names, once the whole of it no longer fits', () => {
        render(<Strip withPrimary room={450} />);

        expect(fitOf()).toBe('floating');
    });

    it('draws the symbols alone once the names do not fit even without the primary control', () => {
        render(<Strip withPrimary room={300} />);

        expect(fitOf()).toBe('symbols');
    });

    it('goes straight from names to symbols on a strip that has no primary control to give up', () => {
        const { rerender } = render(<Strip withPrimary={false} room={450} />);

        expect(fitOf()).toBe('labelled');

        rerender(<Strip withPrimary={false} room={300} />);

        expect(fitOf()).toBe('symbols');
    });

    it('brings the names back when the room comes back, from what they needed the last time they were drawn', () => {
        const { rerender } = render(<Strip withPrimary room={300} />);

        expect(fitOf()).toBe('symbols');

        rerender(<Strip withPrimary room={1000} />);

        expect(fitOf()).toBe('labelled');
    });

    it('measures the names again once what the strip holds changes while it stands as symbols', () => {
        const { rerender } = render(<Strip withPrimary room={300} />);

        expect(fitOf()).toBe('symbols');

        rerender(<Strip withPrimary room={1000} contents="pl" />);

        expect(fitOf()).toBe('labelled');
    });

    it('settles once the typeface arrives rather than flipping between two forms for ever', async () => {
        const fonts = { ready: Promise.resolve() };

        Object.defineProperty(document, 'fonts', { configurable: true, value: fonts });

        try {
            render(<Strip withPrimary room={450} />);

            await act(async () => {
                await fonts.ready;
            });

            expect(fitOf()).toBe('floating');
        } finally {
            Reflect.deleteProperty(document, 'fonts');
        }
    });

    it('reads a strip nothing has laid out yet as fitting, which is what a runtime without layout is', () => {
        Object.defineProperty(Element.prototype, 'clientWidth', { configurable: true, value: 0 });

        render(<Strip withPrimary room={0} />);

        expect(fitOf()).toBe('labelled');
    });
});
