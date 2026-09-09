// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { standingViews, type StandingView } from '../messageList/listing';
import { useListedMail } from '../messageList/useListedMail';
import { useAiFiltersShown } from '../preferences/aiFilters';

// The section the design project draws under the mailbox tree: three standing views of the mailbox, over what
// MailFathom's own reading of the mail already produced.
//
// Each entry is a shortcut to filter criteria and nothing else. Pressing one narrows the list in front of the reader
// by what a derivation recorded, exactly as the filter panel does, and the criteria then stand in that panel where
// every other narrowing stands — removable one at a time and changeable there. There is no second way of asking the
// deployment for mail behind these, and nothing here derives anything: what a view can find is what MailFathom already
// read, so a deployment that has read nothing answers each of them with a folder narrowed to no mail.
//
// The design draws no reaction and no lit state on these entries — only the folder rows above them carry one — so
// nothing here draws a view as being in force. What says so is the filter panel's own count and the criteria in it,
// which is where the design does draw what a list is narrowed by.

const views: Readonly<Record<StandingView, { readonly icon: IconName; readonly label: MessageKey }>> = {
    needsDecision: { icon: 'pending_actions', label: 'aiFilters.needsDecision' },
    commitments: { icon: 'handshake', label: 'aiFilters.commitments' },
    deadlinesThisWeek: { icon: 'schedule', label: 'aiFilters.deadlinesThisWeek' },
};

export function AiFilters({ folded }: { readonly folded: boolean }) {
    const { translate } = useLocalization();
    const listed = useListedMail();
    const shown = useAiFiltersShown();

    // Turned off is the section gone rather than a heading with nothing under it: what it says when it is off is
    // nothing at all, and a heading over three missing entries would say the views had failed.
    if (!shown) {
        return null;
    }

    return (
        <section
            aria-label={translate('aiFilters.heading')}
            className="mt-4 flex flex-col gap-1.5 border-t border-line pt-3.5"
        >
            {/* The heading names a group a reader can see the whole of; the rail draws the group as its symbols and the
                section's own name is what a reader who cannot see them is given instead. */}
            {folded ? null : (
                <p className="px-2.75 text-xs tracking-widest text-muted uppercase">{translate('aiFilters.heading')}</p>
            )}

            {standingViews.map((view) => (
                <button
                    key={view}
                    type="button"
                    title={translate('aiFilters.entryTitle', { view: translate(views[view].label) })}
                    className={`flex cursor-pointer items-center rounded-md text-sm text-text-soft transition hover:bg-hover ${
                        folded ? 'justify-center self-center px-1.75 py-1.5' : 'gap-2.5 px-2.75 py-1.25'
                    }`}
                    onClick={() => {
                        listed.stand(view);
                    }}
                >
                    <Icon name={views[view].icon} className={folded ? 'size-5.75 text-muted' : 'size-4.5 text-muted'} />

                    {folded ? (
                        <span className="sr-only">{translate(views[view].label)}</span>
                    ) : (
                        <span className="min-w-0 flex-1 truncate text-start">{translate(views[view].label)}</span>
                    )}
                </button>
            ))}
        </section>
    );
}
