// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import { MessageView, MessageViewWarning } from './MessageView';

// Two segments a reader picks between are radios rather than buttons, which is what this file is mostly about: the
// chosen one has to *report* itself as chosen, and the arrow keys have to move between the two. Both are properties of
// the element rather than of how it is painted, so a screen drawn with two pressed-looking buttons would pass every
// other assertion here and fail the one that matters to somebody not looking at it.

function inForce(
    embeddedHtmlMessages: boolean,
    chooseMessageView: (chosen: boolean) => void,
): ClientPreferencesInForce {
    return {
        notStated: false,
        telemetryEnabled: true,
        openMailInTabs: false,
        markReadOnOpen: true,
        expandWholeThread: false,
        embeddedHtmlMessages,
        chooseTheme: () => undefined,
        chooseTelemetry: () => undefined,
        chooseTabMode: () => undefined,
        chooseThreadExpansion: () => undefined,
        chooseMessageView,
    };
}

function drawing(embeddedHtmlMessages: boolean, chooseMessageView: (chosen: boolean) => void = () => undefined): void {
    render(
        <LocalizationProvider>
            <MessageView preferences={inForce(embeddedHtmlMessages, chooseMessageView)} />
        </LocalizationProvider>,
    );
}

describe('MessageView', () => {
    it('offers the two views as a group a reader picks one of', () => {
        drawing(false);

        expect(screen.getByRole('radio', { name: 'Reduced' })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'HTML' })).toBeDefined();
    });

    it('reports the reduced text as chosen for somebody who has set nothing', () => {
        drawing(false);

        expect(screen.getByRole('radio', { name: 'Reduced', checked: true })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'HTML', checked: false })).toBeDefined();
    });

    it('reports the sender’s own markup as chosen once it has been picked', () => {
        drawing(true);

        expect(screen.getByRole('radio', { name: 'HTML', checked: true })).toBeDefined();
    });

    it('states the view a reader picked', () => {
        const picked = vi.fn();

        drawing(false, picked);
        fireEvent.click(screen.getByRole('radio', { name: 'HTML' }));

        expect(picked).toHaveBeenCalledWith(true);
    });

    it('states the reduced text again when a reader picks it back', () => {
        const picked = vi.fn();

        drawing(true, picked);
        fireEvent.click(screen.getByRole('radio', { name: 'Reduced' }));

        expect(picked).toHaveBeenCalledWith(false);
    });

    it('says what the chosen view does rather than describing both at once', () => {
        drawing(false);

        expect(
            screen.getByText(
                'Messages are shown as cleaned-up text; the full HTML is one control away on the message head.',
            ),
        ).toBeDefined();
    });

    it('says nothing about the risk itself, which closes the section rather than sitting inside the control', () => {
        drawing(true);

        expect(screen.queryByText(/^A security risk/u)).toBeNull();
    });
});

describe('MessageViewWarning', () => {
    function warning(embeddedHtmlMessages: boolean): void {
        render(
            <LocalizationProvider>
                <MessageViewWarning preferences={inForce(embeddedHtmlMessages, () => undefined)} />
            </LocalizationProvider>,
        );
    }

    it('warns what the sender’s own markup carries where that is what a message is drawn as', () => {
        warning(true);

        expect(screen.getByText(/^A security risk/u)).toBeDefined();
    });

    it('says nothing to a reader whose messages are the reduced text', () => {
        warning(false);

        expect(screen.queryByText(/^A security risk/u)).toBeNull();
    });
});
