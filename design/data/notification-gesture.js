// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.notificationGestureRows = [
  { icon: "mail", tone: "accent" },
  { icon: "event", tone: "ok" },
  { icon: "task_alt" },
  { icon: "topic", tone: "accent" },
  { icon: "group" },
];

MailFathomDesign.data.notificationGestureNumbers = [
  { value: "1:1", name: "Follows the finger", text: "The panel sits where the finger is — no smoothing, no inertia, no delay. The zero point is set the moment the gesture is taken over, not on touch, so the panel never jumps by the slop distance." },
  { value: "= distance", name: "Scrim opacity", text: "Scrim opacity is simply the distance travelled divided by panel height: 0 when closed, 1 when open. The same number in both directions of the gesture." },
  { value: "0.32", name: "Distance threshold", text: "The gesture commits once the finger has travelled more than 0.32 of the panel height — measured from where the gesture was taken over, not from the screen edge." },
  { value: "0.5 px/ms", name: "Velocity threshold", text: "A short flick above 0.5 px/ms commits the gesture even below the distance threshold. Velocity is measured over the last segment of movement; a flick in the opposite direction always beats distance." },
  { value: "260 ms", name: "Spring back", text: "When neither distance nor velocity was met, the panel returns in 260 ms on a cubic-bezier(.32,.72,0,1) curve. That single curve serves both directions of return." },
  { value: "12 / 10 px", name: "Bar and row slop", text: "12 px upward takes the tap away from the nav bar; 10 px downward takes the long press away from a row. The first threshold is larger because the bar has a tap target and a mistake costs a jump to another screen." },
];

MailFathomDesign.data.notificationGestureHandovers = [
  {
    icon: "swipe_down", tone: "accent", name: "A scrolled list hands the gesture to the panel",
    text: "When the notification list is not at its top, a downward move scrolls the list first. The panel does not move a single pixel.",
    rule: "One drag, without lifting the finger: the moment the list reaches the top, the same move becomes a panel dismissal. The zero point is set right then — the remaining distance counts from there, so the panel does not jump to meet the finger.",
  },
  {
    icon: "touch_app", tone: "ok", name: "Row: drag or long press",
    text: "Movement beyond 10 px cancels the 420 ms long press — the row context menu will not open.",
    rule: "Tight the other way too: once the menu has opened, that same finger no longer starts a panel dismissal, and a new dismissal gesture does not start while the menu is open. Never both, never neither.",
  },
  {
    icon: "swipe_up", name: "The nav bar hands the gesture to the panel",
    text: "Up to 12 px upward the gesture belongs to the bar item it started on — releasing is an ordinary tap and navigates to that screen.",
    rule: "After 12 px the bar hands over: the tap is cancelled and will not fire on release, and the panel comes out from under the finger. A downward move started on the bar belongs to nobody — nothing happens.",
  },
];
