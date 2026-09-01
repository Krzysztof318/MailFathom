// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import { isOfferedLocale, localeNames, locales } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';
import { isThemeChoice, themeChoices } from '../theme/themeChoice';
import { useTheme } from '../theme/useTheme';

// The two settings that belong to the person rather than to the deployment. They sit in the header rather than on the
// navigation the prototype puts the theme control on, because that navigation is the bottom bar of a narrow window —
// where a third kind of control cannot go, and where a control that vanished with the width would be one the reader
// can no longer reach. The header is the one place present at both widths.

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
