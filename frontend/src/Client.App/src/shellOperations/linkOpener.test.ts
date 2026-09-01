// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { linkOpenerForThisApplication } from './linkOpener';

// The one branch on a head in this whole tree, so it is the one thing worth proving about it: which operation the
// composition root resolves, and that a link never reaches the document either way.

const tauriBridge = '__TAURI_INTERNALS__';

afterEach(() => {
    Reflect.deleteProperty(globalThis, tauriBridge);
    vi.restoreAllMocks();
});

function shellAnnouncing(): { invoked: { command: string; argument: unknown }[] } {
    const invoked: { command: string; argument: unknown }[] = [];

    Reflect.set(globalThis, tauriBridge, {
        invoke: (command: string, argument: unknown) => {
            invoked.push({ command, argument });

            return Promise.resolve();
        },
    });

    return { invoked };
}

describe('linkOpenerForThisApplication', () => {
    it('opens a link in a new browsing context where no shell offered the command', async () => {
        // A browser answers `window.open` with nothing whenever `noopener` was asked for, which is every call this
        // makes — so the answer is what a real one gives, and succeeding on it is the behaviour being pinned down.
        const opened = vi.spyOn(window, 'open').mockReturnValue(null);

        await linkOpenerForThisApplication()('https://example.invalid/offer');

        expect(opened).toHaveBeenCalledWith('https://example.invalid/offer', '_blank', 'noopener,noreferrer');
    });

    it('hands the link to the shell where one offered the command, so no window here navigates anywhere', async () => {
        const { invoked } = shellAnnouncing();
        const opened = vi.spyOn(window, 'open');

        await linkOpenerForThisApplication()('https://example.invalid/offer');

        expect(invoked.map((call) => call.command)).toEqual(['plugin:opener|open_url']);
        expect(invoked[0]?.argument).toMatchObject({ url: 'https://example.invalid/offer' });
        expect(opened).not.toHaveBeenCalled();
    });
});
