// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { isOfferedLocale, localeNames, locales } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';
import { isThemeChoice, themeChoices } from '../theme/themeChoice';
import { useTheme } from '../theme/useTheme';
import { useWideEnoughForTabs } from './useWideWorkspace';

// The settings a person reaches without leaving the screen they are on. Two of them follow the person and are held on
// the deployment — the theme and the tab mode — and one follows the machine, which is the language: what a client is
// read in is a fact about where somebody is sitting rather than about them.
//
// Two shapes of the theme control stand here rather than one, and that is the design project's decision rather than
// drift. Inside the frame it is the segmented row the account menu draws, where three choices are worth three targets;
// on the sign-in screen it is the dropdown below, which is what a strip carrying a version, a theme, and a language
// above a form has room for.

const themeNames: Readonly<Record<(typeof themeChoices)[number], MessageKey>> = {
    system: 'theme.system',
    light: 'theme.light',
    dark: 'theme.dark',
};

const segmentNames: Readonly<Record<(typeof themeChoices)[number], MessageKey>> = {
    system: 'theme.automatic',
    light: 'theme.light',
    dark: 'theme.dark',
};

const choiceStyle = 'rounded-md border border-line bg-panel px-2 py-1 text-sm text-text-soft transition hover:bg-hover';

const settingRow = 'flex items-center gap-2.5 px-3.25 py-2.5 text-base';

/**
 * The theme as three segments, one of them carrying the accent.
 *
 * Radio buttons rather than buttons with a role written onto them: the platform already announces three of them as one
 * group of choices, moves between them with the arrow keys, and leaves one tab stop where a row of buttons would leave
 * three. Each input is hidden from sight rather than from the accessibility tree, and the label it names carries both
 * the accent that says which is in force and the ring that says which has focus.
 */
export function ThemeSegments({ onChoose }: { readonly onChoose: (choice: (typeof themeChoices)[number]) => void }) {
    const { translate } = useLocalization();
    const { choice } = useTheme();

    return (
        <fieldset className="flex flex-col gap-1.75 px-3.25 py-2.5">
            <legend className="flex items-center gap-2.5 text-base">
                <Icon name="dark_mode" className="size-4.75" />
                {translate('shell.theme')}
            </legend>

            <div className="flex gap-0.75 rounded-xl border border-line bg-rail p-0.5">
                {themeChoices.map((offered) => (
                    <label
                        key={offered}
                        className={`flex-1 cursor-pointer rounded-lg py-1 text-center text-sm transition has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent ${
                            choice === offered ? 'bg-accent font-semibold text-on-accent' : 'text-muted hover:bg-hover'
                        }`}
                    >
                        <input
                            type="radio"
                            name="theme"
                            value={offered}
                            checked={choice === offered}
                            className="sr-only"
                            onChange={(event) => {
                                if (isThemeChoice(event.target.value)) {
                                    onChoose(event.target.value);
                                }
                            }}
                        />
                        {translate(segmentNames[offered])}
                    </label>
                ))}
            </div>
        </fieldset>
    );
}

/**
 * The switch that decides whether opening a message opens a tab.
 *
 * Below the width the tab mode needs it is inert rather than absent, and says so on a line of its own: a control that
 * disappeared by width alone would leave somebody who had turned it on with no way to reach it, and a disabled control
 * saying nothing about why is the same defect worn differently.
 */
export function TabModeSwitch({ on, onChange }: { readonly on: boolean; readonly onChange: (on: boolean) => void }) {
    const { translate } = useLocalization();
    const wideEnough = useWideEnoughForTabs();

    return (
        <label className={`${settingRow} ${wideEnough ? 'cursor-pointer hover:bg-hover' : 'opacity-50'}`}>
            <Icon name="tab" className="size-4.75" />

            <span className="flex min-w-0 flex-1 flex-col gap-px">
                {translate('shell.tabMode')}
                {wideEnough ? null : <span className="text-xs text-faint">{translate('shell.tabModeTooNarrow')}</span>}
            </span>

            {/* The platform has no switch element, so a checkbox carries the role: `switch` is what says this is on or
                off rather than ticked, and everything a checkbox already gives — the keyboard, the label, the
                disabled state — is kept. The track and the knob are drawn from the checked state of the input beside
                them rather than from a second copy of it held in React. */}
            <input
                type="checkbox"
                role="switch"
                checked={on}
                disabled={!wideEnough}
                className="peer sr-only"
                onChange={(event) => {
                    onChange(event.target.checked);
                }}
            />
            <span className="flex w-7.5 shrink-0 items-center rounded-full bg-line-strong p-0.5 transition peer-checked:justify-end peer-checked:bg-accent peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent">
                <span className="size-3.25 rounded-full bg-panel" />
            </span>
        </label>
    );
}

/** The theme as a dropdown, which is the form the sign-in screen carries it in. */
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
