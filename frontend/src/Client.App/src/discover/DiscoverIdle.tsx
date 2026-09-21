// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// What Discover shows before anybody has asked anything: three questions somebody can press instead of typing one.
//
// **Nothing here runs on its own.** The design project also draws a greeting, a briefing of the day written by the model
// and a list of what needs this person today — all of it derived before anybody asked, which is mail read on nobody's
// request and provider spend nobody authorized. #1174 defers that as a question about unattended work, so this screen
// draws the part of the idle state that costs nothing until it is pressed.
//
// **A suggestion is a sentence rather than a plan.** The design draws the plan the service would choose beside each
// one; which plan answers a question is the deployment's decision and is not known until a run has started, so a label
// here would be this client's guess at it.

// The questions offered, in the order the design draws them: something owed, something agreed, and something to find.
// They are catalogue entries because they are sentences somebody reads, and they are stated here rather than read from
// the deployment because nothing on the client surface publishes a suggestion.
const suggestions: readonly MessageKey[] = [
    'discover.suggestion.owed',
    'discover.suggestion.agreed',
    'discover.suggestion.files',
];

/**
 * The screen before a question, which is a list of questions.
 *
 * @param onAsk Asks one of them, exactly as typing it into the field and pressing *Ask* would.
 */
export function DiscoverIdle({ onAsk }: { readonly onAsk: (question: string) => void }) {
    const { translate } = useLocalization();

    return (
        <section aria-labelledby="discover-suggestions" className="flex max-w-2xl flex-col gap-2.5">
            <h2 className="text-2xs tracking-widest text-muted uppercase" id="discover-suggestions">
                {translate('discover.tryThis')}
            </h2>

            <ul className="flex flex-col gap-2">
                {suggestions.map((suggestion) => {
                    const question = translate(suggestion);

                    return (
                        <li key={suggestion}>
                            <button
                                className="w-full rounded-lg border border-line bg-panel px-3.5 py-3 text-start text-md text-pretty transition hover:border-accent"
                                type="button"
                                onClick={() => {
                                    onAsk(question);
                                }}
                            >
                                {question}
                            </button>
                        </li>
                    );
                })}
            </ul>
        </section>
    );
}
