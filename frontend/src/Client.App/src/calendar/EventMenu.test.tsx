// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { CalendarEvent } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { AgentHandOverContext, type AgentHandOver } from '../routing/agentHandOver';
import { EventMenu } from './EventMenu';

const event = {
    id: '0198f4a1-0000-7000-8000-00000000c001',
    title: 'Review with Anna',
} as unknown as CalendarEvent;

function menuUnder(handToAgent: ((handOver: AgentHandOver) => void) | null): void {
    render(
        <LocalizationProvider>
            <AgentHandOverContext value={handToAgent}>
                <EventMenu
                    event={event}
                    at={{ x: 20, y: 30 }}
                    onSelect={vi.fn()}
                    onOpen={vi.fn()}
                    onAskReminders={vi.fn()}
                    onAskDeletion={vi.fn()}
                    onClose={vi.fn()}
                />
            </AgentHandOverContext>
        </LocalizationProvider>,
    );
}

describe('EventMenu', () => {
    it('hands the event over to the agent, before the deletion as the design draws it', () => {
        const handToAgent = vi.fn();
        menuUnder(handToAgent);

        expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
            expect.stringContaining('Select events'),
            expect.stringContaining('Open the event'),
            expect.stringContaining('Reminders'),
            expect.stringContaining('Ask the agent'),
            expect.stringContaining('Delete the event'),
        ]);

        fireEvent.click(screen.getByRole('menuitem', { name: 'Ask the agent' }));

        expect(handToAgent).toHaveBeenCalledWith({
            scope: { kind: 'calendarEvent', subject: event.id },
            title: 'Review with Anna',
        });
    });

    it('offers no way to the agent for a credential that has no agent to reach', () => {
        menuUnder(null);

        expect(screen.queryByRole('menuitem', { name: 'Ask the agent' })).toBeNull();
    });
});
