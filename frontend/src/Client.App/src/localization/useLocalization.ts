// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { MessageKey } from './en';
import type { Locale } from './locale';

// The context and its hook sit apart from the provider that fills them because a module Vite hot-reloads may export
// components alone, which `react-refresh/only-export-components` is what states. So this file is what a screen reads
// the active language through, and `Localization.tsx` beside it is what decides what that language is.

/** Reads one message under the active locale, filling each `{name}` hole from `values`. */
export type Translate = (key: MessageKey, values?: Readonly<Record<string, string>>) => string;

export interface Localization {
    readonly locale: Locale;
    readonly setLocale: (locale: Locale) => void;
    readonly translate: Translate;
}

export const LocalizationContext = createContext<Localization | null>(null);

export function useLocalization(): Localization {
    const localization = useContext(LocalizationContext);

    if (localization === null) {
        throw new Error('A component read the localization outside the LocalizationProvider that main.tsx mounts.');
    }

    return localization;
}
