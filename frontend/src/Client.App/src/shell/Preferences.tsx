// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import { isOfferedLocale, localeNames, locales } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';
import { isThemeChoice, themeChoices } from '../theme/themeChoice';
import { useTheme } from '../theme/useTheme';

// The two settings that belong to the person rather than to the deployment. Inside the frame they sit in the account
// menu, which is where the design project puts what is about the person, and on the sign-in screen they stand on their
// own: they belong to somebody who has not signed in yet exactly as much as to somebody who has.

const themeNames: Readonly<Record<(typeof themeChoices)[number], MessageKey>> = {
    system: 'theme.system',
    light: 'theme.light',
    dark: 'theme.dark',
};

const choiceStyle = 'rounded-md border border-line bg-panel px-2 py-1 text-sm text-text-soft transition hover:bg-hover';

export function ThemeChoice() {
    const { translate } = useLocalization();
    const { choice, setThemeChoice } = useTheme();

    return (
        <select
            aria-label={translate('shell.theme')}
            className={choiceStyle}
            value={choice}
            onChange={(event) => {
                if (isThemeChoice(event.target.value)) {
                    setThemeChoice(event.target.value);
                }
            }}
        >
            {themeChoices.map((offered) => (
                <option key={offered} value={offered}>
                    {translate(themeNames[offered])}
                </option>
            ))}
        </select>
    );
}

export function LanguageChoice() {
    const { locale, setLocale, translate } = useLocalization();

    return (
        <select
            aria-label={translate('shell.language')}
            className={choiceStyle}
            value={locale}
            onChange={(event) => {
                if (isOfferedLocale(event.target.value)) {
                    setLocale(event.target.value);
                }
            }}
        >
            {locales.map((offered) => (
                <option key={offered} value={offered}>
                    {localeNames[offered]}
                </option>
            ))}
        </select>
    );
}
