// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.data.tasks = [
  { id: "t1", t: "Send the 5% CPI cap counter-proposal", when: "today", day: "27", group: "Today", src: "Contract addendum — signatures", tid: "contoso", ai: true, est: "45 min" },
  { id: "t2", t: "Reply to Marta about the calculation", when: "today", day: "27", group: "Today", src: "CPI calculation 2027", tid: "marta", ai: true, est: "20 min" },
  { id: "t3", t: "Confirm the Fabrikam delivery date", when: "02.09", day: "02", group: "This week", src: "Q4 delivery dates", tid: "fabrikam1", ai: true, est: "15 min" },
  { id: "t4", t: "Prepare the IT contract list for audit", when: "03.09", day: "03", group: "This week", src: "Contract register request", tid: "audyt1", ai: true, est: "2 hrs" },
  { id: "t5", t: "Answer Adventure Works on weekend SLA", when: "01.09", day: "01", group: "This week", src: "Question about weekend SLA", tid: "klient3", ai: true, est: "30 min" },
  { id: "t6", t: "Close the 2027 budget after the Contoso decision", when: "10.09", day: "10", group: "Later", src: "CPI calculation 2027", tid: "marta", ai: false, est: "1 hr" },
  { id: "t7", t: "Sign the Northwind NDA", when: "12.09", day: "12", group: "Later", src: "Draft NDA — Northwind", tid: "prawnik2", ai: false, est: "10 min" },
];

// How full today is, after the count of open tasks.
MailFathomDesign.data.taskCapacity = { meetings: "2 meetings", advice: "It fits if you start with the CPI counter-proposal." };
