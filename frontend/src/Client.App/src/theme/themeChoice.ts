// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { deviceKeys, deviceStore } from '../device/deviceStore';

// Which theme was chosen is one of the things the device holds rather than the deployment, so it is read and written
// through `device/deviceStore.ts` — including the handling a browser or a WebView refusing storage needs, which is
// that module's rather than this one's. The choice survives a restart of either head because both store it in the same
// place: the web bundle in the browser's origin storage, the desktop shell in its WebView's.

/** What a person may choose: one of the two themes, or whatever the machine is set to. */
export const themeChoices = ['system', 'light', 'dark'] as const;

export type ThemeChoice = (typeof themeChoices)[number];

/** What is actually painted. `system` resolves to one of these before anything reads it. */
export type Theme = 'light' | 'dark';

/** What a first run resolves to when nothing it reads names a choice: the machine's own. */
export const defaultThemeChoice: ThemeChoice = 'system';

const darkQuery = '(prefers-color-scheme: dark)';

export function isThemeChoice(value: unknown): value is ThemeChoice {
    return typeof value === 'string' && (themeChoices as readonly string[]).includes(value);
}

/** The theme explicitly chosen on this machine, or `null` where none was chosen or what was stored is not offered. */
export function readStoredThemeChoice(): ThemeChoice | null {
    const stored = deviceStore().read(deviceKeys.themeChoice);

    return isThemeChoice(stored) ? stored : null;
}

export function storeThemeChoice(choice: ThemeChoice): void {
    deviceStore().write(deviceKeys.themeChoice, choice);
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
