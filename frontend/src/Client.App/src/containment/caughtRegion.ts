// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Component } from 'react';
import { Containment } from './Containment';
import type { ClientRegion } from '../telemetry/clientTelemetry';

// This sits apart from `Containment.tsx` because a module Vite hot-reloads may export components alone, which is the
// same reason `localization/useLocalization.ts` stands apart from the provider beside it.

/**
 * Which region a boundary that contained a failure stands around, read from the boundary React hands over with it.
 *
 * `onCaughtError` names the component that caught, and this is what turns that into the closed value a record may
 * carry. Anything else — a boundary React did not name, or one this client did not place — is reported as the whole
 * application, which is what a failure nothing narrower contained actually cost.
 */
export function regionOf(boundary: Component<unknown> | undefined): ClientRegion {
    return boundary instanceof Containment ? boundary.props.region : 'application';
}
