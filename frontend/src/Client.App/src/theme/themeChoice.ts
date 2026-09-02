// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/** What a person may choose: one of the two themes, or whatever the machine is set to. */
export const themeChoices = ['system', 'light', 'dark'] as const;

export type ThemeChoice = (typeof themeChoices)[number];

/** What is actually painted. `system` resolves to one of these before anything reads it. */
export type Theme = 'light' | 'dark';

/** What a first run resolves to when nothing it reads names a choice: the machine's own. */
export const defaultThemeChoice: ThemeChoice = 'system';

// What the explicit choice is written under, beside the language. It survives a restart of either head because both
// store it in the same place: the web bundle in the browser's origin storage, the desktop shell in its WebView's.
//
// Reached as `window.localStorage` rather than as the bare global, for the reason `localization/locale.ts` gives.
const storageKey = 'mailfathom.theme';

const darkQuery = '(prefers-color-scheme: dark)';

export function isThemeChoice(value: unknown): value is ThemeChoice {
    return typeof value === 'string' && (themeChoices as readonly string[]).includes(value);
}

/** The theme explicitly chosen on this machine, or `null` where none was chosen or what was stored is not offered. */
export function readStoredThemeChoice(): ThemeChoice | null {
    try {
        const stored = window.localStorage.getItem(storageKey);
        return isThemeChoice(stored) ? stored : null;
    } catch {
        return null;
    }
}

export function storeThemeChoice(choice: ThemeChoice): void {
    try {
        window.localStorage.setItem(storageKey, choice);
    } catch {
        // A browser configured to refuse storage still runs the client; the choice then lasts the session rather than
        // outliving it, which is a smaller loss than a screen that fails to mount over a preference.
    }
}

/** What the client opens in: the explicit choice, else following the machine. */
export function preferredThemeChoice(): ThemeChoice {
    return readStoredThemeChoice() ?? defaultThemeChoice;
}

/** Whether the machine itself is set to a dark appearance right now. */
export function machinePrefersDark(): boolean {
    return window.matchMedia(darkQuery).matches;
}

/**
 * Calls back whenever the machine's own appearance changes, and answers with the function that stops listening.
 * Only worth subscribing to while the choice is to follow it; an explicit choice is unaffected by the machine.
 */
export function watchMachineTheme(changed: () => void): () => void {
    const query = window.matchMedia(darkQuery);

    query.addEventListener('change', changed);

    return () => {
        query.removeEventListener('change', changed);
    };
}
