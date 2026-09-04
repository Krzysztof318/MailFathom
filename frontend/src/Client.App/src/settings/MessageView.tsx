// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { ChoiceSegment } from '../controls/ChoiceSegment';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';

// Which of the two reading surfaces a message opens on, as the design project draws it: two segments, the chosen one
// carrying the accent, and a line beneath saying what the choice does — which is a different sentence per choice
// rather than one sentence describing the control.
//
// The segments themselves are `controls/ChoiceSegment.tsx`, which is what the theme and the language are drawn from as
// well; what stands here is the pill around them and the sentence under it.
//
// The warning is the design project's and belongs to the HTML choice alone. It says what the reduced view is
// protecting somebody from, and it is careful about the same distinction the confirmation on the message head is: the
// markup is drawn in isolation, and a message drawn at all can still tell its sender it was opened. It is exported
// separately because the design closes the section with it, below the thread-expansion switch rather than between that
// switch and the control it is about — a caution reads as the section's last word rather than as a line inside it.

/** Which of the two the segment stands for, in the order the design project puts them in. */
const views = ['reduced', 'embeddedHtml'] as const;

type MessageViewChoice = (typeof views)[number];

const viewNames: Readonly<Record<MessageViewChoice, MessageKey>> = {
    reduced: 'settings.messageViewReduced',
    embeddedHtml: 'settings.messageViewHtml',
};

const viewExplanations: Readonly<Record<MessageViewChoice, MessageKey>> = {
    reduced: 'settings.messageViewReducedExplanation',
    embeddedHtml: 'settings.messageViewHtmlExplanation',
};

export function MessageView({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { translate } = useLocalization();
    const chosen: MessageViewChoice = preferences.embeddedHtmlMessages ? 'embeddedHtml' : 'reduced';

    return (
        <>
            <fieldset className="flex gap-0.75 rounded-xl border border-line-strong bg-sunken p-0.75">
                <legend className="sr-only">{translate('settings.messageView')}</legend>

                {views.map((offered) => (
                    <ChoiceSegment
                        key={offered}
                        shape="section"
                        name="message-view"
                        value={offered}
                        chosen={chosen === offered}
                        onChoose={(picked) => {
                            preferences.chooseMessageView(picked === 'embeddedHtml');
                        }}
                    >
                        {translate(viewNames[offered])}
                    </ChoiceSegment>
                ))}
            </fieldset>

            <p className="text-xs text-muted">{translate(viewExplanations[chosen])}</p>
        </>
    );
}

/** What drawing a sender's own markup exposes somebody to, said where the choice to draw it has been made. */
export function MessageViewWarning({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { translate } = useLocalization();

    if (!preferences.embeddedHtmlMessages) {
        return null;
    }

    return (
        <p className="flex items-start gap-2 rounded-xl border border-warning bg-warning-soft px-2.5 py-2.25 text-xs text-warning-text">
            <Icon name="gpp_maybe" className="size-4 shrink-0" />
            {translate('settings.messageViewHtmlWarning')}
        </p>
    );
}
