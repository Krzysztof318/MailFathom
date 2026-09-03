// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import { Switch } from '../controls/Switch';
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
// Two shapes of each control stand here rather than one, and that is the design project's decision rather than drift.
// Inside the frame each is the full-width row the account menu and the settings screen draw. On the sign-in screen
// both are the compact segmented pickers below: the same radio group in the same pill, sized for a strip above a form
// rather than for a menu, because the screen a person meets before any mail is where the two settings that belong to
// the machine rather than to the deployment have to be reachable at a glance.

const segmentNames: Readonly<Record<(typeof themeChoices)[number], MessageKey>> = {
    system: 'theme.automatic',
    light: 'theme.light',
    dark: 'theme.dark',
};

const settingRow = 'flex items-center gap-2.5 px-3.25 py-2.5 text-base';

// The compact pill the sign-in screen's two pickers stand in, and one segment inside it. Written once here because the
// two are one shape in the design project and a second arrangement of the same utilities is how a client stops looking
// like one product.
const compactGroup = 'flex gap-0.75 rounded-lg border border-line bg-sunken p-0.5';

const compactSegment =
    'cursor-pointer rounded-md px-2 py-1 text-xs transition has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent';

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

            <Switch on={on} disabled={!wideEnough} onChange={onChange} />
        </label>
    );
}

/**
 * The theme as the compact segmented picker the sign-in screen carries it in.
 *
 * The symbol beside it is the one the client already draws for the theme rather than a second one meaning the same
 * thing, and it is decorative: the group's own legend is what names the setting.
 */
export function ThemeChoice() {
    const { translate } = useLocalization();
    const { choice, setThemeChoice } = useTheme();

    return (
        <fieldset className="flex items-center gap-1.5">
            <legend className="sr-only">{translate('shell.theme')}</legend>
            <Icon name="dark_mode" className="size-4 text-faint" />

            <div className={compactGroup}>
                {themeChoices.map((offered) => (
                    <label
                        key={offered}
                        className={`${compactSegment} ${
                            choice === offered ? 'bg-accent font-semibold text-on-accent' : 'text-muted hover:bg-hover'
                        }`}
                    >
                        <input
                            type="radio"
                            name="theme-choice"
                            value={offered}
                            checked={choice === offered}
                            className="sr-only"
                            onChange={(event) => {
                                if (isThemeChoice(event.target.value)) {
                                    setThemeChoice(event.target.value);
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
 * The language as one chip per offering, which is the form the settings screen carries it in.
 *
 * The second shape of this control rather than a second control, exactly as the theme has two: a strip above a sign-in
 * form has room for a dropdown and a settings section has room for the choices themselves. Radio buttons for the
 * reason the theme segments are — the platform announces them as one group, moves between them with the arrow keys,
 * and leaves one tab stop.
 *
 * The languages are never translated. Somebody who has landed in one they cannot read finds their own by its own name.
 */
export function LanguageSegments() {
    const { locale, setLocale, translate } = useLocalization();

    return (
        <fieldset className="flex flex-wrap gap-1.25">
            <legend className="sr-only">{translate('shell.language')}</legend>

            {locales.map((offered) => (
                <label
                    key={offered}
                    className={`cursor-pointer rounded-lg px-3.25 py-1.5 text-sm transition has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent ${
                        locale === offered
                            ? 'bg-accent font-semibold text-on-accent'
                            : 'border border-line bg-sunken text-text-soft hover:bg-hover'
                    }`}
                >
                    <input
                        type="radio"
                        name="language"
                        value={offered}
                        checked={locale === offered}
                        className="sr-only"
                        onChange={(event) => {
                            if (isOfferedLocale(event.target.value)) {
                                setLocale(event.target.value);
                            }
                        }}
                    />
                    {localeNames[offered]}
                </label>
            ))}
        </fieldset>
    );
}

/** The language as the same compact picker, which is the other half of the sign-in screen's strip. */
export function LanguageChoice() {
    const { locale, setLocale, translate } = useLocalization();

    return (
        <fieldset className="flex items-center gap-1.5">
            <legend className="sr-only">{translate('shell.language')}</legend>
            <Icon name="language" className="size-4 text-faint" />

            <div className={compactGroup}>
                {locales.map((offered) => (
                    <label
                        key={offered}
                        className={`${compactSegment} ${
                            locale === offered ? 'bg-accent font-semibold text-on-accent' : 'text-muted hover:bg-hover'
                        }`}
                    >
                        <input
                            type="radio"
                            name="language-choice"
                            value={offered}
                            checked={locale === offered}
                            className="sr-only"
                            onChange={(event) => {
                                if (isOfferedLocale(event.target.value)) {
                                    setLocale(event.target.value);
                                }
                            }}
                        />
                        {localeNames[offered]}
                    </label>
                ))}
            </div>
        </fieldset>
    );
}
