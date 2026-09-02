// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { catalogues, preferredLocale, storeLocale, type Locale } from './locale';
import { LocalizationContext, type Localization } from './useLocalization';

// The whole of the client's localization, and it is this small because the platform already carries the expensive half.
// `Intl` formats dates, numbers, relative times, lists, and plural categories in every engine both heads render in, so
// what is left for MailFathom to own is a catalogue, a lookup, and a hole to fill — which is less code than the
// configuration a library would be adopted with, and it costs the bundle and the licence register nothing.
//
// It lives in `Client.App` because language is a presentation concern. `Client.Backend` carries no catalogue and no
// locale: what it answers with is a closed set of reasons and states, which the catalogue is what names.
//
// English is the fallback, in the two places something can resolve to nothing. A *locale* that does falls back in
// `locale.ts`, where the preference is narrowed to what is carried. A *key* that does cannot happen at all: every
// catalogue is typed against `en`, so a key one language declares and another does not fails `pnpm typecheck` rather
// than reaching a screen — which is why there is no runtime lookup fallback here to read as dead code.

export function LocalizationProvider({ children }: { readonly children: ReactNode }) {
    const [locale, setLocale] = useState(preferredLocale);

    // What assistive technology reads the document's language from, and what a browser picks a hyphenation and a
    // spelling dictionary by. React re-renders on the state change above, so the switch applies without a restart and
    // this keeps the document's own declaration in step with what is on the screen.
    useEffect(() => {
        document.documentElement.lang = locale;
    }, [locale]);

    const localization = useMemo<Localization>(
        () => ({
            locale,
            setLocale: (chosen: Locale) => {
                storeLocale(chosen);
                setLocale(chosen);
            },
            translate: (key, values) => fill(catalogues[locale][key], values),
        }),
        [locale],
    );

    return <LocalizationContext value={localization}>{children}</LocalizationContext>;
}

function fill(message: string, values: Readonly<Record<string, string>> | undefined): string {
    if (values === undefined) {
        return message;
    }

    // A hole nobody filled stays on the screen as `{name}` rather than disappearing, so a caller that forgot one sees
    // it instead of reading a sentence with a gap where a value should have been.
    return message.replace(/\{(\w+)\}/g, (hole: string, name: string) => values[name] ?? hole);
}
