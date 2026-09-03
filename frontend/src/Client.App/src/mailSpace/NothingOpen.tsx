// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import { BrandMark } from '../controls/BrandMark';
import { useLocalization } from '../localization/useLocalization';

// What the content pane holds when a person working in tabs has closed all of them: what the space is for, and the one
// way back into it there is. The design project offers three ways out of here; two of them — writing a message and
// asking about the whole correspondence — are screens this client does not have yet, so what is drawn is the one that
// exists rather than two controls that would answer nothing.
//
// Closing the last tab is a view change, so focus comes here: the strip that had it is no longer on the screen, and a
// reader on a keyboard would otherwise be left on nothing. Opening the client with nothing open is a landing rather
// than a navigation and moves nothing, which is what `arriving` separates.

export function NothingOpen({
    arriving,
    onReopenLastRead,
}: {
    /** Whether this replaced something rather than being what the space opened with. */
    readonly arriving: boolean;

    /** Opens the message that was being read last, or `null` where nothing has been read yet. */
    readonly onReopenLastRead: (() => void) | null;
}) {
    const { translate } = useLocalization();
    const region = useRef<HTMLDivElement>(null);

    useEffect(() => {
        if (arriving) {
            region.current?.focus();
        }
    }, [arriving]);

    return (
        <div
            ref={region}
            tabIndex={-1}
            className="flex min-h-full flex-col items-center justify-center gap-4 bg-sunken px-8 py-8 text-center"
        >
            <BrandMark className="size-11.5" />

            <div className="flex max-w-md flex-col gap-1.75">
                <p className="text-3xl font-semibold tracking-tight">{translate('tabs.nothingOpen')}</p>
                <p className="text-md text-pretty text-muted">{translate('tabs.nothingOpenExplanation')}</p>
            </div>

            {onReopenLastRead === null ? null : (
                <button
                    type="button"
                    className="rounded-lg border border-line bg-panel px-4 py-2.25 text-base text-text-soft transition hover:bg-hover"
                    onClick={onReopenLastRead}
                >
                    {translate('tabs.reopenLastRead')}
                </button>
            )}
        </div>
    );
}
