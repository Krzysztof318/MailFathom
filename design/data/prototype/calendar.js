// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.calendarDays = [
  { dow: "MON", d: "24", today: false, ev: [
    { time: "11:00", h: 11, t: "Call with Wrona Law Office", kind: "meeting", src: "Legal opinion — indexation cap" },
  ] },
  { dow: "TUE", d: "25", today: false, ev: [
    { time: "12:30", h: 12, t: "CPI calculation from Marta", kind: "mail", src: "CPI calculation 2027" },
  ] },
  { dow: "WED", d: "26", today: false, ev: [
    { time: "09:00", h: 9, t: "Fabrikam rollout review", kind: "meeting", src: "Q4 delivery dates" },
  ] },
  { dow: "THU", d: "27", today: true, ev: [
    { time: "10:00", h: 10, t: "Reply to Marta — calculation", kind: "deadline", src: "CPI calculation 2027" },
    { time: "15:00", h: 15, t: "Rollout status with Piotr", kind: "meeting", src: "Re: rollout schedule" },
  ] },
  { dow: "FRI", d: "28", today: false, ev: [
    { time: "all day", h: 0, t: "Decision: Contoso addendum", kind: "deadline", src: "Contract addendum — signatures" },
    { time: "13:00", h: 13, t: "Call with Anna Kowalska", kind: "meeting", src: "Contract addendum — signatures" },
  ] },
  { dow: "SAT", d: "29", today: false, ev: [
    { time: "all day", h: 0, t: "Payment of invoice 08/2026", kind: "deadline", src: "Invoice 08/2026" },
  ] },
  { dow: "SUN", d: "30", today: false, ev: [] },
];


MailFathomDesign.data.proposedEvents = [
  { id: "p1", t: "Confirm the Fabrikam delivery date", when: "02.09", src: "Q4 delivery dates" },
  { id: "p2", t: "IT contract list for audit", when: "03.09", src: "Contract register request" },
  { id: "p3", t: "Satisfaction survey", when: "05.09", src: "HR Department" },
];

MailFathomDesign.data.focusBlocks = [
  { day: "27", time: "16:00", len: "45 min", t: "Draft the CPI counter-proposal", why: "The decision is due tomorrow and the draft is not ready." },
  { day: "28", time: "09:15", len: "30 min", t: "Review the addendum before the call", why: "The call with Anna is at 13:00 — better to go in with a position." },
];

// The sample week as dates, for reading a day out of a typed event.
MailFathomDesign.data.weekdayDates = { "monday": "24", "tuesday": "25", "wednesday": "26", "thursday": "27", "friday": "28", "saturday": "29", "sunday": "30", "today": "27", "tomorrow": "28", "day after tomorrow": "29" };

// The line above the calendar.
MailFathomDesign.data.calendarBriefing = "One critical deadline today and 2 meetings. The Contoso addendum decision is due tomorrow — the draft reply still is not ready.";

// What an event made from the open thread starts with.
MailFathomDesign.data.eventFromThread = { day: "28", date: "28.08", time: "13:00" };
