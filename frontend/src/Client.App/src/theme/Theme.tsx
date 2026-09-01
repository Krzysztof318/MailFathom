// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useLayoutEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';
import {
    machinePrefersDark,
    preferredThemeChoice,
    storeThemeChoice,
    watchMachineTheme,
    type ThemeChoice,
} from './themeChoice';
import { ThemeContext, type Themed } from './useTheme';

// Which of the two themes is painted is decided here and nowhere else. A screen composes against the semantic tokens in
// `styles.css`, both themes declare the same names, and this writes the one attribute that picks between the two — so
// no component ever asks which theme is in force, exactly as none asks which language is.

export function ThemeProvider({ children }: { readonly children: ReactNode }) {
    const [choice, setChoice] = useState(preferredThemeChoice);

    // Following the machine means the answer changes while the client is open, so the preference is subscribed to
    // rather than read once. `useSyncExternalStore` is what React reads a value living outside it through, and it
    // leaves the theme derived during render instead of a second piece of state kept in step with this one.
    const machineIsDark = useSyncExternalStore(watchMachineTheme, machinePrefersDark);
    const theme = choice === 'system' ? (machineIsDark ? 'dark' : 'light') : choice;

    // A layout effect rather than an ordinary one, because this runs before the browser paints: a reader whose machine
    // is dark would otherwise see one light frame on every cold start, which is the flash a theme is chosen to avoid.
    useLayoutEffect(() => {
        document.documentElement.dataset['theme'] = theme;
    }, [theme]);

    const themed = useMemo<Themed>(
        () => ({
            choice,
            theme,
            setThemeChoice: (chosen: ThemeChoice) => {
                storeThemeChoice(chosen);
                setChoice(chosen);
            },
        }),
        [choice, theme],
    );

    return <ThemeContext value={themed}>{children}</ThemeContext>;
}
