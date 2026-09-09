// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailEnrichmentMark } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { readingNames } from './messageReadings';

// What MailFathom made of a message, on the row that stands for it. It is the design project's own line: a small mark
// reading `AI` and then one sentence, clipped where it does not fit, in the line the row's height already reserves.
//
// **It never displaces the mail.** Who wrote, what about, and when are the two lines above it and are untouched by
// this; a row with a reading is the same height as a row without one, and a folder of enriched mail shows exactly as
// many messages per screen as a plain one. That is the density decision this component exists to keep: the alternative
// was a second line that wraps, which would have cost a list a third of its rows to add a sentence.
//
// **It says what it is rather than only showing it.** A screen reader meets the aspect by name — a commitment, why the
// message matters, or what it is about — because a sentence announced bare inside a row reads as part of the mail, and
// the whole point of the mark beside it is that this one is not.
//
// **It is not a control.** The row is an `option` of a listbox, which holds no focusable descendant of its own, and
// its height is what the window above it does arithmetic over — so a mark that expanded where it stands would break
// both. Where the reading is checked is the row's own menu, which a pointer, a finger and a keyboard all already
// reach; `ReadingsAsked.tsx` is what it opens.

export function MessageReading({ mark }: { readonly mark: MailEnrichmentMark }) {
    const { translate } = useLocalization();

    return (
        <span className="flex items-center gap-1.5">
            {/* Hidden from the accessibility tree because the words beside it say the same thing at more length: two
                statements about the same mark would be read out one after the other. */}
            <span
                aria-hidden="true"
                className="shrink-0 rounded-xs border border-line-strong px-1.25 text-2xs leading-none text-muted"
            >
                {translate('ai.badge')}
            </span>

            {/* The whole sentence is announced once, aspect and all, and the line that shows it is hidden from the
                accessibility tree — the alternative is two nodes read one after the other with the punctuation
                between them written into the markup, which is a sentence assembled at the call site. */}
            <span className="sr-only">
                {translate('reading.said', {
                    aspect: translate(readingNames[mark.aspect]),
                    text: mark.text,
                })}
            </span>

            <span aria-hidden="true" className="truncate">
                {mark.text}
            </span>
        </span>
    );
}
