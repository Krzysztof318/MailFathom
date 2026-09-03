// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { largestPortraitOctets } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { Settings } from './Settings';

const settings: ClientPreferencesInForce = {
    openMailInTabs: false,
    telemetryEnabled: true,
    notStated: false,
    chooseTheme: () => undefined,
    chooseTabMode: () => undefined,
    chooseTelemetry: () => undefined,
};

const named: OwnProfileInForce = {
    displayName: 'Ada Lovelace',
    changeable: true,
    picture: null,
    nameNotAcceptable: false,
    nameNotStated: false,
    pictureNotStated: false,
    correctName: () => undefined,
    choosePicture: () => undefined,
    removePicture: () => undefined,
};

function renderSettings({
    profile = named,
    preferences = settings,
    onClose = () => undefined,
}: {
    readonly profile?: OwnProfileInForce;
    readonly preferences?: ClientPreferencesInForce;
    readonly onClose?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <Settings open profile={profile} preferences={preferences} onClose={onClose} />
        </LocalizationProvider>,
    );
}

/** One file of a stated kind and size, which is the whole of what the client judges a chosen picture by. */
function file(name: string, type: string, octets: number): File {
    return new File([new Uint8Array(octets)], name, { type });
}

function choose(picture: File): void {
    fireEvent.change(screen.getByLabelText('Picture'), { target: { files: [picture] } });
}

describe('Settings', () => {
    it('is a dialog named for what it holds', () => {
        renderSettings();

        expect(screen.getByRole('dialog', { name: 'Settings' })).toBeDefined();
    });

    it('draws the name this deployment records the person under', () => {
        renderSettings();

        expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveProperty('value', 'Ada Lovelace');
    });

    it('sends a corrected name once the person has moved on from the field', () => {
        const correctName = vi.fn();
        renderSettings({ profile: { ...named, correctName } });

        const field = screen.getByRole('textbox', { name: 'Full name' });
        fireEvent.change(field, { target: { value: 'Grace Hopper' } });
        fireEvent.blur(field);

        expect(correctName).toHaveBeenCalledWith('Grace Hopper');
    });

    it('sends nothing where the field was left holding the name it was given', () => {
        const correctName = vi.fn();
        renderSettings({ profile: { ...named, correctName } });

        fireEvent.blur(screen.getByRole('textbox', { name: 'Full name' }));

        expect(correctName).not.toHaveBeenCalled();
    });

    it('draws the name as read-only, with the reason, where this deployment will not take a change of it', () => {
        renderSettings({ profile: { ...named, changeable: false } });

        expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveProperty('readOnly', true);
        expect(screen.getByText(/keeps your name/u)).toBeDefined();
    });

    it('still offers the picture, which this deployment grants on reading mail rather than on the name', () => {
        const choosePicture = vi.fn();
        renderSettings({
            profile: { ...named, changeable: false, picture: 'data:image/png;base64,AA==', choosePicture },
        });

        const picture = file('portrait.png', 'image/png', 64);
        choose(picture);

        expect(choosePicture).toHaveBeenCalledWith(picture, 'image/png');
        expect(screen.getByRole('button', { name: 'Remove' })).toBeDefined();
    });

    it('sends nothing from a read-only field, whatever a person manages to leave in it', () => {
        const correctName = vi.fn();
        renderSettings({ profile: { ...named, changeable: false, correctName } });

        const field = screen.getByRole('textbox', { name: 'Full name' });
        fireEvent.change(field, { target: { value: 'Grace Hopper' } });
        fireEvent.blur(field);

        expect(correctName).not.toHaveBeenCalled();
    });

    it('says which bounds a picture has to meet before one is chosen', () => {
        renderSettings();

        expect(screen.getByText('JPG/PNG, up to 1 MB')).toBeDefined();
    });

    it('sends a picture of a kind this surface stores, under the kind it is', () => {
        const choosePicture = vi.fn();
        renderSettings({ profile: { ...named, choosePicture } });

        const picture = file('portrait.png', 'image/png', 64);
        choose(picture);

        expect(choosePicture).toHaveBeenCalledWith(picture, 'image/png');
    });

    it('refuses a file that is neither kind at the control rather than sending it', () => {
        const choosePicture = vi.fn();
        renderSettings({ profile: { ...named, choosePicture } });

        choose(file('portrait.gif', 'image/gif', 64));

        expect(choosePicture).not.toHaveBeenCalled();
        expect(screen.getByText(/neither a JPEG nor a PNG/u)).toBeDefined();
    });

    it('refuses a picture over a megabyte at the control rather than sending it', () => {
        const choosePicture = vi.fn();
        renderSettings({ profile: { ...named, choosePicture } });

        choose(file('portrait.png', 'image/png', largestPortraitOctets + 1));

        expect(choosePicture).not.toHaveBeenCalled();
        expect(screen.getByText(/larger than 1 MB/u)).toBeDefined();
    });

    it('clears the refusal once an admissible picture is chosen instead', () => {
        renderSettings();

        choose(file('portrait.gif', 'image/gif', 64));
        choose(file('portrait.png', 'image/png', 64));

        expect(screen.queryByText(/neither a JPEG nor a PNG/u)).toBeNull();
    });

    it('offers removal only to somebody who has a picture to remove', () => {
        renderSettings({ profile: { ...named, picture: null } });

        expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull();
    });

    it('removes the picture, which is what falls back to the initials', () => {
        const removePicture = vi.fn();
        renderSettings({ profile: { ...named, picture: 'data:image/png;base64,AA==', removePicture } });

        fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

        expect(removePicture).toHaveBeenCalledOnce();
    });

    it('says when the deployment would not take the name that was typed', () => {
        renderSettings({ profile: { ...named, nameNotAcceptable: true } });

        expect(screen.getByText(/was not accepted/u)).toBeDefined();
    });

    it('says when a change of the name did not reach the deployment at all', () => {
        renderSettings({ profile: { ...named, nameNotStated: true } });

        expect(screen.getByText(/not saved to the deployment/u)).toBeDefined();
    });

    it('says when a picture did not reach the deployment', () => {
        renderSettings({ profile: { ...named, pictureNotStated: true } });

        expect(screen.getByText(/was not saved to the deployment/u)).toBeDefined();
    });

    it('draws telemetry as the decision to withhold it, so the switch being on is the private answer', () => {
        renderSettings({ preferences: { ...settings, telemetryEnabled: false } });

        expect(screen.getByRole('switch', { name: /Do not send telemetry/u })).toHaveProperty('checked', true);
    });

    it('hands the switch to what holds the preference, stated as what may be sent', () => {
        const chooseTelemetry = vi.fn();
        renderSettings({ preferences: { ...settings, chooseTelemetry } });

        fireEvent.click(screen.getByRole('switch', { name: /Do not send telemetry/u }));

        expect(chooseTelemetry).toHaveBeenCalledWith(false);
    });

    it('says what withholding telemetry costs, and only while it is withheld', () => {
        renderSettings({ preferences: { ...settings, telemetryEnabled: true } });

        expect(screen.queryByText(/support is harder/u)).toBeNull();
    });

    it('says what withholding telemetry costs once it is withheld', () => {
        renderSettings({ preferences: { ...settings, telemetryEnabled: false } });

        expect(screen.getByText(/support is harder/u)).toBeDefined();
    });

    it('offers the language here rather than in the menu that leads here', () => {
        renderSettings();

        expect(screen.getByRole('group', { name: 'Language' })).toBeDefined();
    });

    it('closes from its own control, which is the way out that does not need a keyboard', () => {
        const onClose = vi.fn();
        renderSettings({ onClose });

        fireEvent.click(screen.getByRole('button', { name: 'Close settings' }));

        expect(onClose).toHaveBeenCalledOnce();
    });
});
