// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientMessageView } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import { MessageView, MessageViewWarning } from './MessageView';

// Three segments a reader picks between are radios rather than buttons, which is what this file is mostly about: the
// chosen one has to *report* itself as chosen, and the arrow keys have to move between the three. Both are properties
// of the element rather than of how it is painted, so a screen drawn with three pressed-looking buttons would pass
// every other assertion here and fail the one that matters to somebody not looking at it.

function inForce(
    messageView: ClientMessageView,
    chooseMessageView: (chosen: ClientMessageView) => void,
): ClientPreferencesInForce {
    return {
        notStated: false,
        telemetryEnabled: true,
        openMailInTabs: false,
        markReadOnOpen: true,
        expandWholeThread: false,
        aiFiltersShown: true,
        messageView,
        notificationSeconds: 5,
        chooseTheme: () => undefined,
        chooseTelemetry: () => undefined,
        chooseTabMode: () => undefined,
        chooseThreadExpansion: () => undefined,
        chooseMessageView,
        chooseAiFilters: () => undefined,
        chooseNotificationSeconds: () => undefined,
    };
}

function drawing(
    messageView: ClientMessageView,
    chooseMessageView: (chosen: ClientMessageView) => void = () => undefined,
): void {
    render(
        <LocalizationProvider>
            <MessageView preferences={inForce(messageView, chooseMessageView)} />
        </LocalizationProvider>,
    );
}

describe('MessageView', () => {
    it('offers the three views as a group a reader picks one of', () => {
        drawing('reduced');

        expect(screen.getByRole('radio', { name: 'AI simplified' })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Simplified' })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Original' })).toBeDefined();
    });

    it('reports the reduced text as chosen for somebody who has set nothing', () => {
        drawing('reduced');

        expect(screen.getByRole('radio', { name: 'Simplified', checked: true })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'AI simplified', checked: false })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Original', checked: false })).toBeDefined();
    });

    it('reports the cleaned rendering as chosen once it has been picked', () => {
        drawing('cleaned');

        expect(screen.getByRole('radio', { name: 'AI simplified', checked: true })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Simplified', checked: false })).toBeDefined();
    });

    it('reports the sender’s own markup as chosen once it has been picked', () => {
        drawing('embeddedHtml');

        expect(screen.getByRole('radio', { name: 'Original', checked: true })).toBeDefined();
    });

    it.each<ClientMessageView>(['cleaned', 'reduced', 'embeddedHtml'])(
        'states %s as the view a reader picked',
        (picked) => {
            const stated = vi.fn();
            const from: ClientMessageView = picked === 'reduced' ? 'embeddedHtml' : 'reduced';
            const names: Readonly<Record<ClientMessageView, string>> = {
                cleaned: 'AI simplified',
                reduced: 'Simplified',
                embeddedHtml: 'Original',
            };

            drawing(from, stated);
            fireEvent.click(screen.getByRole('radio', { name: names[picked] }));

            expect(stated).toHaveBeenCalledWith(picked);
        },
    );

    it('says what the chosen view does rather than describing all three at once', () => {
        drawing('reduced');

        expect(
            screen.getByText(
                'Messages are shown as cleaned-up text; the original is one control away on the message head.',
            ),
        ).toBeDefined();
    });

    // What the reader has to be told about the third rendering is that nothing is rewritten, because a model deciding
    // anything about a message is read as a model having written it.
    it('says the cleaned rendering never rewrites a word', () => {
        drawing('cleaned');

        expect(screen.getByText(/it never rewrites a word/u)).toBeDefined();
    });

    it('says nothing about the risk itself, which closes the section rather than sitting inside the control', () => {
        drawing('embeddedHtml');

        expect(screen.queryByText(/^A security risk/u)).toBeNull();
    });
});

describe('MessageViewWarning', () => {
    function warning(messageView: ClientMessageView): void {
        render(
            <LocalizationProvider>
                <MessageViewWarning preferences={inForce(messageView, () => undefined)} />
            </LocalizationProvider>,
        );
    }

    it('warns what the sender’s own markup carries where that is what a message is drawn as', () => {
        warning('embeddedHtml');

        expect(screen.getByText(/^A security risk/u)).toBeDefined();
    });

    it('says nothing to a reader whose messages are the reduced text', () => {
        warning('reduced');

        expect(screen.queryByText(/^A security risk/u)).toBeNull();
    });

    // The cleaned rendering is the same closed document tree with blocks dropped, so nothing the sender wrote reaches
    // the screen that the reduced view would not have drawn — and a caution about markup there would be a caution about
    // something nobody is being shown.
    it('says nothing to a reader whose messages are cleaned, which draws no markup either', () => {
        warning('cleaned');

        expect(screen.queryByText(/^A security risk/u)).toBeNull();
    });
});
