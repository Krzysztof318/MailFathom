// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { wordInstantRange } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { inTimeOrder } from './calendarSpan';

// The dates the reader's mail named that are not on their calendar yet, which the design project draws down the side
// of every view. Each one is accepted or let go of, and both are the deployment's own routes: accepting states that
// the date is now a fact, and letting one go removes the row — a date nobody wanted is not worth keeping, which is why
// the surface publishes no third answer.
//
// **What is drawn here is what falls inside the span being read**, for the reason `useCalendarWindow.ts` gives: the
// deployment answers a window, and a column claiming to show everything while reading one week would be this client
// inventing a second question.
//
// The design draws a second group above this one — time the deployment suggests blocking out, with *Block time* and
// *Not now* beneath each — and `/api/client` publishes nothing behind it: no route proposes a slot, and the reading
// that would produce one is not something this screen may derive. So it is left out rather than drawn as a card whose
// controls would do nothing, and closing that gap is a route of its own rather than a group to invent here.

export function ProposedDates({
    proposals,
    onAccept,
    onDismiss,
}: {
    /** What the reader's mail proposed inside the span being read, less anything already answered. */
    readonly proposals: readonly CalendarEvent[];

    readonly onAccept: (proposal: CalendarEvent) => void;
    readonly onDismiss: (proposal: CalendarEvent) => void;
}) {
    const { locale, translate } = useLocalization();

    return (
        <section
            aria-label={translate('calendar.proposedHeading')}
            className="flex shrink-0 flex-col gap-2.75 border-line bg-sunken px-4 py-3.5 panes:w-calendar-side panes:overflow-y-auto panes:border-s"
        >
            <h3 className="flex items-center gap-2.25 text-2xs tracking-widest text-muted uppercase">
                {translate('calendar.proposedHeading')}
                <span className="rounded-sm bg-accent px-1.5 py-0.5 text-2xs tracking-normal text-on-accent normal-case">
                    {translate('calendar.authored')}
                </span>
            </h3>

            <p className="text-sm text-muted text-pretty">{translate('calendar.proposedExplained')}</p>

            {proposals.length === 0 ? (
                <p className="text-sm text-faint text-pretty">{translate('calendar.nothingProposed')}</p>
            ) : (
                <ul className="flex flex-col gap-2.25">
                    {inTimeOrder(proposals).map((proposal) => (
                        <li
                            key={proposal.id}
                            className="flex flex-col gap-1.5 rounded-xl border border-dashed border-accent-line bg-panel px-3 py-2.75"
                        >
                            <p className="flex items-baseline gap-2.25">
                                <span className="min-w-0 flex-1 text-sm text-pretty">{proposal.title}</span>
                                <span className="shrink-0 text-xs font-semibold whitespace-nowrap text-accent-deep">
                                    {wordInstantRange(proposal.start, proposal.end, locale, 'stamp')}
                                </span>
                            </p>

                            {proposal.sourceMessage === null ? null : (
                                <p className="flex items-center gap-1 text-2xs text-muted">
                                    <Icon name="open_in_new" className="size-3.5" />
                                    {translate('calendar.fromMail')}
                                </p>
                            )}

                            <div className="flex flex-wrap gap-1.75">
                                <SecondaryButton
                                    label={translate('calendar.acceptProposal')}
                                    shape="compact"
                                    onActivate={() => {
                                        onAccept(proposal);
                                    }}
                                />

                                <SecondaryButton
                                    label={translate('calendar.dismissProposal')}
                                    shape="compact"
                                    onActivate={() => {
                                        onDismiss(proposal);
                                    }}
                                />
                            </div>
                        </li>
                    ))}
                </ul>
            )}
        </section>
    );
}
