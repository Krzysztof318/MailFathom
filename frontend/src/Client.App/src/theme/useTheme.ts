// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { Theme, ThemeChoice } from './themeChoice';

// The context and its hook sit apart from the provider that fills them for the reason `localization/useLocalization.ts`
// gives: a module Vite hot-reloads may export components alone.

export interface Themed {
    /** What the person chose, which may be to follow the machine. */
    readonly choice: ThemeChoice;

    /** What that choice actually paints right now. */
    readonly theme: Theme;

    readonly setThemeChoice: (choice: ThemeChoice) => void;
}

export const ThemeContext = createContext<Themed | null>(null);

export function useTheme(): Themed {
    const themed = useContext(ThemeContext);

    if (themed === null) {
        throw new Error('A component read the theme outside the ThemeProvider that main.tsx mounts.');
    }

    return themed;
}
