// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { addressOf, spaceLabels, spaces, type Space } from '../routing/spaces';

// One list of links, laid out two ways by the width it is given: a rail down the side of a wide window, and bottom
// navigation across a narrow one. Nothing here asks which head it is running on, and nothing disappears at either
// width — the same three destinations are present in both, which is what makes the two shapes one navigation.
//
// Links rather than buttons, because these navigate: the browser then supplies the keyboard path, the history entry,
// and opening one in a window of its own, none of which a click handler would have.

const spaceGlyphs: Readonly<Record<Space, string>> = {
    discover: 'M11 3a8 8 0 1 0 4.9 14.3l3.9 3.9 1.4-1.4-3.9-3.9A8 8 0 0 0 11 3Zm0 2a6 6 0 1 1 0 12 6 6 0 0 1 0-12Z',
    mail: 'M3 6.5A1.5 1.5 0 0 1 4.5 5h15A1.5 1.5 0 0 1 21 6.5v11a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5v-11ZM5.6 7 12 11.7 18.4 7H5.6ZM19 8.9l-6.4 4.7a1 1 0 0 1-1.2 0L5 8.9V17h14V8.9Z',
    cases: 'M9 3h6a2 2 0 0 1 2 2v1h3a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h3V5a2 2 0 0 1 2-2Zm0 3h6V5H9v1ZM4 8v10h16V8H4Z',
};

export function SpaceNavigation({ current }: { readonly current: Space }) {
    const { translate } = useLocalization();

    return (
        <nav
            aria-label={translate('shell.spaces')}
            className="flex shrink-0 justify-around gap-1 border-t border-line bg-rail p-1 workspace:order-first workspace:w-24 workspace:flex-col workspace:justify-start workspace:gap-2 workspace:border-t-0 workspace:border-e workspace:p-3"
        >
            {spaces.map((space) => (
                <SpaceLink key={space} space={space} current={space === current} />
            ))}
        </nav>
    );
}

function SpaceLink({ space, current }: { readonly space: Space; readonly current: boolean }) {
    const { translate } = useLocalization();

    return (
        <a
            href={addressOf(space)}
            aria-current={current ? 'page' : undefined}
            className={`flex flex-1 flex-col items-center gap-1 rounded-lg px-2 py-2 text-xs font-medium transition workspace:flex-none ${
                current ? 'bg-accent-soft text-accent-strong' : 'text-muted hover:bg-hover hover:text-text'
            }`}
        >
            <svg viewBox="0 0 24 24" aria-hidden="true" className="size-5 fill-current">
                <path d={spaceGlyphs[space]} />
            </svg>
            {translate(spaceLabels[space])}
        </a>
    );
}
