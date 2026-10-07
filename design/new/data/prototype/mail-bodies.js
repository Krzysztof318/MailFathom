// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

/* The original messages as their senders wrote them — what actually arrives from the server; the thread window
   shows cleaned-up text and the original opens on demand (the "code" icon) in a separate tab / preview. */
MailFathomDesign.data.mailBodies = {
  contosoComparison: { title: "Addendum — comparison version", html: `
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef1f5;padding:24px 12px">
 <tr><td align="center">
  <table role="presentation" width="640" cellpadding="0" cellspacing="0" style="width:640px;max-width:100%;background:#ffffff;border:1px solid #d7dee7">
   <tr><td style="background:#14293f;padding:22px 28px">
     <table role="presentation" width="100%"><tr>
       <td style="color:#ffffff;font-size:19px;letter-spacing:.06em">CONTOSO <span style="font-weight:normal;opacity:.7">LEGAL</span></td>
       <td align="right" style="color:#9fb3c8;font-size:12px;font-family:Arial,sans-serif">Document no. AN-2026/03</td>
     </tr></table>
   </td></tr>
   <tr><td style="padding:26px 28px 6px 28px">
     <p style="margin:0 0 14px 0;font-size:15px;line-height:1.6">Dear Karolina,</p>
     <p style="margin:0 0 18px 0;font-size:15px;line-height:1.6">attached is the comparison version of the addendum. Below is a summary of the changes against the master agreement of 14.06.2021.</p>
     <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-family:Arial,sans-serif;font-size:13px">
       <tr style="background:#f2f5f9">
         <th align="left" style="padding:9px 10px;border:1px solid #d7dee7;width:26%">Provision</th>
         <th align="left" style="padding:9px 10px;border:1px solid #d7dee7">In force</th>
         <th align="left" style="padding:9px 10px;border:1px solid #d7dee7">After change</th>
       </tr>
       <tr><td style="padding:9px 10px;border:1px solid #d7dee7">§ 4.2 — response time</td><td style="padding:9px 10px;border:1px solid #d7dee7;color:#8a94a6"><s>4 hours</s></td><td style="padding:9px 10px;border:1px solid #d7dee7;background:#fff8e1"><strong>2 hours</strong></td></tr>
       <tr><td style="padding:9px 10px;border:1px solid #d7dee7">Schedule 2 — remuneration</td><td style="padding:9px 10px;border:1px solid #d7dee7;color:#8a94a6"><s>base flat fee</s></td><td style="padding:9px 10px;border:1px solid #d7dee7;background:#fff8e1"><strong>+ 8% from 01.01.2027</strong></td></tr>
       <tr><td style="padding:9px 10px;border:1px solid #d7dee7">§ 11 — CPI indexation</td><td style="padding:9px 10px;border:1px solid #d7dee7">uncapped</td><td style="padding:9px 10px;border:1px solid #d7dee7">uncapped</td></tr>
     </table>
     <p style="margin:18px 0 0 0;font-size:13px;line-height:1.6;font-family:Arial,sans-serif;color:#52606d">Comments due: <strong style="color:#9a3412">28.08.2026</strong>. After that date we send the document for electronic signature.</p>
   </td></tr>
   <tr><td style="padding:18px 28px 26px 28px">
     <table role="presentation" cellpadding="0" cellspacing="0"><tr>
       <td style="background:#14293f;border-radius:3px"><a href="#" style="display:inline-block;padding:11px 20px;color:#ffffff;text-decoration:none;font-family:Arial,sans-serif;font-size:13px">Open in the document portal</a></td>
       <td style="padding-left:12px;font-family:Arial,sans-serif;font-size:12px;color:#8a94a6">valid 14 days</td>
     </tr></table>
   </td></tr>
   <tr><td style="border-top:1px solid #e3e8ee;padding:16px 28px;font-family:Arial,sans-serif;font-size:11px;line-height:1.6;color:#8a94a6">
     Contoso Ltd, 51 Prosta St, 00-838 Warsaw · Reg. 0000123456 · VAT 525-000-00-00<br>
     This message contains confidential information. <a href="#" style="color:#4a6fa5">Data processing policy</a>
   </td></tr>
  </table>
 </td></tr>
</table>` },

  cpiCalculation: { title: "CPI calculation 2027", html: `
<div style="max-width:620px;margin:0 auto;padding:24px 16px;font-family:Arial,Helvetica,sans-serif">
 <div style="background:#ffffff;border:1px solid #dde3ea;border-top:4px solid #2f6f4f">
  <div style="padding:20px 24px 8px 24px">
   <h1 style="margin:0 0 4px 0;font-size:18px;color:#14293f">Impact of the 8% increase on the 2027 budget</h1>
   <p style="margin:0 0 16px 0;font-size:12px;color:#7b8794">Working calculation · Marta Nowak · 25.08.2026</p>
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px">
    <tr><td style="padding:7px 0;border-bottom:1px solid #eef1f5">Annual flat fee 2026</td><td align="right" style="padding:7px 0;border-bottom:1px solid #eef1f5">€238,000</td></tr>
    <tr><td style="padding:7px 0;border-bottom:1px solid #eef1f5">Contractual increase 8%</td><td align="right" style="padding:7px 0;border-bottom:1px solid #eef1f5">+ €19,040</td></tr>
    <tr><td style="padding:7px 0;border-bottom:1px solid #eef1f5">CPI indexation (4.1% forecast)</td><td align="right" style="padding:7px 0;border-bottom:1px solid #eef1f5">+ €10,542</td></tr>
    <tr><td style="padding:9px 0;font-weight:bold">Total 2027</td><td align="right" style="padding:9px 0;font-weight:bold;color:#9a3412">€267,582</td></tr>
   </table>
   <p style="margin:16px 0 6px 0;font-size:12px;color:#7b8794">Scenarios at different CPI levels</p>
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:12px">
    <tr><td style="width:64px;padding:4px 0">CPI 3%</td><td><div style="background:#cfe3d6;height:14px;width:62%"></div></td><td align="right" style="width:80px">€264,970</td></tr>
    <tr><td style="padding:4px 0">CPI 4.1%</td><td><div style="background:#8fbfa4;height:14px;width:74%"></div></td><td align="right">€267,582</td></tr>
    <tr><td style="padding:4px 0">CPI 6%</td><td><div style="background:#e0a97a;height:14px;width:92%"></div></td><td align="right">€272,093</td></tr>
    <tr><td style="padding:4px 0">CPI 9%</td><td><div style="background:#d98282;height:14px;width:100%"></div></td><td align="right">€279,219</td></tr>
   </table>
   <div style="margin:18px 0 4px 0;padding:12px 14px;background:#fff8e1;border-left:3px solid #d9a441;font-size:13px;line-height:1.6">
    Recommendation: negotiate an <strong>upper indexation cap of 5% per year</strong>. At CPI 9% that saves about €11.6k a year.
   </div>
  </div>
  <div style="border-top:1px solid #eef1f5;padding:12px 24px;font-size:11px;color:#9aa5b1">Attachment: CPI_2027.xlsx · working sheet, not an offer</div>
 </div>
</div>` },

  transferConfirmation: { title: "Transfer confirmation", html: `
<div style="padding:26px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="560" align="center" cellpadding="0" cellspacing="0" style="width:560px;max-width:100%;background:#ffffff;border-radius:6px;overflow:hidden;border:1px solid #d7dee7">
  <tr><td style="background:#0d4f3c;padding:18px 24px;color:#ffffff;font-size:16px;letter-spacing:.04em">BUSINESS BANK</td></tr>
  <tr><td style="padding:24px 24px 8px 24px">
    <p style="margin:0 0 6px 0;font-size:12px;color:#7b8794;text-transform:uppercase;letter-spacing:.08em">Transfer completed</p>
    <p style="margin:0 0 20px 0;font-size:30px;color:#0d4f3c"><strong>€18,400.00</strong></p>
    <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px">
     <tr><td style="padding:8px 0;color:#7b8794;width:38%">Beneficiary</td><td style="padding:8px 0">Contoso Ltd</td></tr>
     <tr><td style="padding:8px 0;color:#7b8794">Account</td><td style="padding:8px 0;font-family:monospace">DE61 1090 1014 0000 0712 19</td></tr>
     <tr><td style="padding:8px 0;color:#7b8794">Reference</td><td style="padding:8px 0">Addendum — August 2026 settlement</td></tr>
     <tr><td style="padding:8px 0;color:#7b8794">Booking date</td><td style="padding:8px 0">01.09.2026, 06:58</td></tr>
     <tr><td style="padding:8px 0;color:#7b8794">Operation ref</td><td style="padding:8px 0;font-family:monospace">TRN-8841-220916</td></tr>
    </table>
  </td></tr>
  <tr><td style="padding:4px 24px 22px 24px">
    <div style="background:#f2f7f4;border:1px solid #cfe3d6;padding:12px 14px;font-size:12px;line-height:1.6;color:#31513f">
     This e-mail confirms the operation. The bank never asks for your password or a payment code by e-mail.
    </div>
  </td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:14px 24px;font-size:11px;color:#9aa5b1;line-height:1.6">
    Business Bank plc · helpline 801 000 000 · <a href="#" style="color:#0d4f3c">Terms</a> · <a href="#" style="color:#0d4f3c">Unsubscribe from alerts</a>
  </td></tr>
 </table>
</div>` },

  travelTickets: { title: "Tickets Kraków–Berlin", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border:1px solid #d7dee7">
  <tr><td style="padding:18px 24px;border-bottom:2px dashed #d7dee7">
    <table width="100%"><tr>
     <td style="font-size:15px;color:#14293f"><strong>BOOKING CONFIRMED</strong></td>
     <td align="right" style="font-size:12px;color:#7b8794">PNR <strong style="color:#14293f;font-family:monospace">7QX4MB</strong></td>
    </tr></table>
  </td></tr>
  <tr><td style="padding:22px 24px">
    <table width="100%" cellpadding="0" cellspacing="0">
     <tr>
      <td style="width:38%"><div style="font-size:30px;color:#14293f">KRK</div><div style="font-size:12px;color:#7b8794">Kraków Balice<br>12.09, 07:40</div></td>
      <td align="center" style="width:24%;color:#9aa5b1;font-size:12px">✈<br>1 h 25 min<br>direct</td>
      <td align="right" style="width:38%"><div style="font-size:30px;color:#14293f">BER</div><div style="font-size:12px;color:#7b8794">Berlin Brandenburg<br>12.09, 09:05</div></td>
     </tr>
    </table>
    <table width="100%" cellpadding="0" cellspacing="0" style="margin-top:20px;border-collapse:collapse;font-size:13px">
     <tr style="background:#f7f9fb"><td style="padding:8px 10px;border:1px solid #e3e8ee">Passenger</td><td style="padding:8px 10px;border:1px solid #e3e8ee">KOWALSKA / KAROLINA</td></tr>
     <tr><td style="padding:8px 10px;border:1px solid #e3e8ee">Fare</td><td style="padding:8px 10px;border:1px solid #e3e8ee">Flex · 23 kg baggage included</td></tr>
     <tr style="background:#f7f9fb"><td style="padding:8px 10px;border:1px solid #e3e8ee">Seat</td><td style="padding:8px 10px;border:1px solid #e3e8ee">14C (aisle)</td></tr>
    </table>
    <div style="margin-top:18px;padding:12px 14px;background:#fdf0e4;border-left:3px solid #d9a441;font-size:13px;line-height:1.6">
     Online check-in opens <strong>11.09 at 07:40</strong> and closes 2 h before departure.
    </div>
    <div style="margin-top:18px"><a href="#" style="display:inline-block;background:#14293f;color:#ffffff;text-decoration:none;padding:12px 22px;font-size:13px">Check in online</a></div>
  </td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:14px 24px;font-size:11px;color:#9aa5b1">Travel Desk · bookings@travel.example · free changes up to 24 h before departure</td></tr>
 </table>
</div>` },

  tradeShowMaterials: { title: "Trade show materials 24.09", html: `
<div style="padding:22px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="620" align="center" cellpadding="0" cellspacing="0" style="width:620px;max-width:100%;background:#ffffff;border:1px solid #dde3ea">
  <tr><td style="background:linear-gradient(90deg,#14293f,#2b5079);padding:26px 26px 22px 26px;color:#ffffff">
    <div style="font-size:11px;letter-spacing:.16em;opacity:.75">MARKETING · INTERNAL</div>
    <div style="font-size:24px;margin-top:6px">Industry Trade Show 2026</div>
    <div style="font-size:13px;opacity:.85;margin-top:4px">24–26.09 · Poznań · stand C18 (18 m²)</div>
  </td></tr>
  <tr><td style="padding:22px 26px 6px 26px">
   <table width="100%" cellpadding="0" cellspacing="0">
    <tr>
     <td width="50%" valign="top" style="padding-right:10px">
      <div style="font-size:13px;font-weight:bold;color:#14293f;margin-bottom:6px">To close this week</div>
      <ul style="margin:0;padding-left:18px;font-size:13px;line-height:1.75;color:#3e4c59">
       <li>Roll-ups — print-ready file <strong>by 10.09</strong></li>
       <li>Product leaflet (PL/EN)</li>
       <li>Demo list and presenter</li>
      </ul>
     </td>
     <td width="50%" valign="top" style="padding-left:10px">
      <div style="font-size:13px;font-weight:bold;color:#14293f;margin-bottom:6px">Who owns what</div>
      <table width="100%" style="font-size:12px;color:#3e4c59;border-collapse:collapse">
       <tr><td style="padding:4px 0">Stand</td><td align="right">P. Zieliński</td></tr>
       <tr><td style="padding:4px 0">Print</td><td align="right">Nord agency</td></tr>
       <tr><td style="padding:4px 0">Demo</td><td align="right" style="color:#9a3412">unassigned</td></tr>
      </table>
     </td>
    </tr>
   </table>
  </td></tr>
  <tr><td style="padding:16px 26px 24px 26px">
    <div style="border:1px solid #e3e8ee;background:#f7f9fb;padding:14px 16px;font-size:13px;line-height:1.6;color:#3e4c59">
     <strong>Printer deadline: 10.09, 12:00.</strong> After that, a 35% rush surcharge applies.
    </div>
    <div style="margin-top:16px"><a href="#" style="display:inline-block;background:#2b5079;color:#ffffff;text-decoration:none;padding:11px 20px;font-size:13px">Open the materials folder</a></div>
  </td></tr>
  <tr><td style="border-top:1px solid #eef1f5;padding:14px 26px;font-size:11px;color:#9aa5b1">Sent to: sales team, marketing · <a href="#" style="color:#2b5079">unsubscribe</a></td></tr>
 </table>
</div>` },

  hostingInvoice: { title: "Invoice 07/2026 — hosting", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border:1px solid #d7dee7">
  <tr><td style="padding:22px 26px;border-bottom:1px solid #eef1f5">
    <table width="100%"><tr>
     <td style="font-size:17px;color:#14293f"><strong>Invoice 07/2026</strong><div style="font-size:12px;color:#7b8794;margin-top:4px">issued 28.08.2026</div></td>
     <td align="right"><div style="display:inline-block;background:#fdf0e4;color:#9a3412;font-size:12px;padding:6px 12px;border-radius:3px">Due: 04.09.2026</div></td>
    </tr></table>
  </td></tr>
  <tr><td style="padding:20px 26px 6px 26px">
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px">
    <tr style="background:#f7f9fb"><th align="left" style="padding:9px 10px;border:1px solid #e3e8ee">Item</th><th align="right" style="padding:9px 10px;border:1px solid #e3e8ee">Net</th><th align="right" style="padding:9px 10px;border:1px solid #e3e8ee">VAT</th><th align="right" style="padding:9px 10px;border:1px solid #e3e8ee">Gross</th></tr>
    <tr><td style="padding:9px 10px;border:1px solid #e3e8ee">Application hosting — August</td><td align="right" style="padding:9px 10px;border:1px solid #e3e8ee">1 600,00</td><td align="right" style="padding:9px 10px;border:1px solid #e3e8ee">368,00</td><td align="right" style="padding:9px 10px;border:1px solid #e3e8ee">1 968,00</td></tr>
    <tr><td style="padding:9px 10px;border:1px solid #e3e8ee">Backups (250 GB)</td><td align="right" style="padding:9px 10px;border:1px solid #e3e8ee">302,44</td><td align="right" style="padding:9px 10px;border:1px solid #e3e8ee">69,56</td><td align="right" style="padding:9px 10px;border:1px solid #e3e8ee">372,00</td></tr>
    <tr><td align="right" colspan="3" style="padding:11px 10px;border:1px solid #e3e8ee;background:#f7f9fb"><strong>Amount due</strong></td><td align="right" style="padding:11px 10px;border:1px solid #e3e8ee;background:#f7f9fb"><strong>€2,340.00</strong></td></tr>
   </table>
   <p style="font-size:12px;color:#7b8794;line-height:1.7;margin:16px 0 0 0">Account: <span style="font-family:monospace">DE27 1140 2004 0000 3002 01</span><br>Please quote the invoice number in the reference.</p>
  </td></tr>
  <tr><td style="padding:16px 26px 24px 26px"><a href="#" style="display:inline-block;background:#14293f;color:#ffffff;text-decoration:none;padding:11px 22px;font-size:13px">Download PDF</a></td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:13px 26px;font-size:11px;color:#9aa5b1">Cloud Provider Ltd · VAT 701-000-00-00 · billing@cloud.example</td></tr>
 </table>
</div>` },


  rolloutSchedule: { title: "Rollout schedule — phase 2", html: `
<div style="padding:22px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="620" align="center" cellpadding="0" cellspacing="0" style="width:620px;max-width:100%;background:#ffffff;border:1px solid #dde3ea">
  <tr><td style="background:#1f3b57;padding:20px 24px;color:#ffffff">
    <div style="font-size:11px;letter-spacing:.14em;opacity:.7">WEEKLY REPORT · NORDWIND ROLLOUT</div>
    <div style="font-size:21px;margin-top:6px">Phase 2 closed — 41 of 58 tasks</div>
  </td></tr>
  <tr><td style="padding:20px 24px 4px 24px">
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px">
    <tr style="background:#f4f7fa"><th align="left" style="padding:8px 10px;border:1px solid #e3e8ee">Phase</th><th align="left" style="padding:8px 10px;border:1px solid #e3e8ee">Date</th><th align="left" style="padding:8px 10px;border:1px solid #e3e8ee">Status</th></tr>
    <tr><td style="padding:8px 10px;border:1px solid #e3e8ee">1 · Analysis and data migration</td><td style="padding:8px 10px;border:1px solid #e3e8ee">completed 12.08</td><td style="padding:8px 10px;border:1px solid #e3e8ee;color:#2f6f4f">✔ accepted</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #e3e8ee">2 · Exchange integration</td><td style="padding:8px 10px;border:1px solid #e3e8ee">31.08</td><td style="padding:8px 10px;border:1px solid #e3e8ee;color:#2f6f4f">✔ ready for acceptance</td></tr>
    <tr style="background:#fff8e1"><td style="padding:8px 10px;border:1px solid #e3e8ee">3 · UAT testing</td><td style="padding:8px 10px;border:1px solid #e3e8ee">date not set</td><td style="padding:8px 10px;border:1px solid #e3e8ee;color:#9a6b12">● awaiting a date</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #e3e8ee">4 · Training and handover</td><td style="padding:8px 10px;border:1px solid #e3e8ee">October</td><td style="padding:8px 10px;border:1px solid #e3e8ee;color:#7b8794">planned</td></tr>
   </table>
   <p style="font-size:12px;color:#7b8794;margin:16px 0 6px 0">Sprint 34 progress</p>
   <div style="background:#eef1f5;height:16px"><div style="background:#1f3b57;height:16px;width:70%"></div></div>
   <table width="100%" style="margin-top:14px;font-size:12px;color:#3e4c59;border-collapse:collapse">
    <tr><td style="padding:5px 0;border-bottom:1px solid #eef1f5">Blocked tasks</td><td align="right" style="border-bottom:1px solid #eef1f5">2 (API Graph)</td></tr>
    <tr><td style="padding:5px 0;border-bottom:1px solid #eef1f5">Critical bugs</td><td align="right" style="border-bottom:1px solid #eef1f5">0</td></tr>
    <tr><td style="padding:5px 0">Schedule risk</td><td align="right" style="color:#9a3412">medium</td></tr>
   </table>
   <div style="margin:18px 0 4px 0;padding:12px 14px;background:#f4f7fa;border-left:3px solid #1f3b57;font-size:13px;line-height:1.6">To decide: the UAT window. I suggest 15–19.09 so we finish before the training.</div>
  </td></tr>
  <tr><td style="padding:14px 24px 22px 24px"><a href="#" style="display:inline-block;background:#1f3b57;color:#ffffff;text-decoration:none;padding:11px 20px;font-size:13px">Open the sprint board</a></td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:13px 24px;font-size:11px;color:#9aa5b1">Generated automatically every Monday · rollout team</td></tr>
 </table>
</div>` },

  legalOpinion: { title: "Legal opinion — indexation cap", html: `
<div style="padding:26px 12px;font-family:Georgia,'Times New Roman',serif">
 <table role="presentation" width="640" align="center" cellpadding="0" cellspacing="0" style="width:640px;max-width:100%;background:#ffffff;border:1px solid #d9d2c5">
  <tr><td style="padding:26px 30px 12px 30px;border-bottom:3px double #b9ae97">
    <div style="font-size:20px;letter-spacing:.14em;color:#3b3428">WRONA LAW OFFICE</div>
    <div style="font-size:11px;letter-spacing:.08em;color:#8c8271;font-family:Arial,sans-serif;margin-top:4px">ATTORNEYS AT LAW · WARSAW</div>
  </td></tr>
  <tr><td style="padding:22px 30px 4px 30px">
    <table width="100%" style="font-family:Arial,sans-serif;font-size:12px;color:#6b6355">
     <tr><td>Ref. WR/2026/218</td><td align="right">Warsaw, 24 August 2026</td></tr>
    </table>
    <h1 style="font-size:17px;margin:18px 0 14px 0;color:#3b3428">Opinion on the introduction of an upper indexation cap</h1>
    <p style="font-size:15px;line-height:1.75;margin:0 0 12px 0">§ 1. Introducing an upper cap on fee indexation is permissible under freedom of contract and requires no change to the other provisions of the master agreement.</p>
    <p style="font-size:15px;line-height:1.75;margin:0 0 12px 0">§ 2. The clause should name the reference index, the moment it is read and the maximum increase per year. Recommended wording is attached.</p>
    <div style="border-left:3px solid #b9ae97;background:#faf7f0;padding:14px 16px;margin:16px 0;font-size:14px;line-height:1.7">
     “Indexation may not exceed 5% per calendar year, regardless of the CPI figure published by the statistics office.”
    </div>
    <p style="font-size:15px;line-height:1.75;margin:0 0 12px 0">§ 3. I rate the litigation risk as low. An uncapped clause with CPI above 8% could, however, materially shift the contractual balance.</p>
    <table width="100%" cellpadding="0" cellspacing="0" style="margin:18px 0 6px 0;border-collapse:collapse;font-family:Arial,sans-serif;font-size:12px">
     <tr style="background:#faf7f0"><th align="left" style="padding:8px 10px;border:1px solid #e6dfd0">Scenario</th><th align="left" style="padding:8px 10px;border:1px solid #e6dfd0">Assessment</th></tr>
     <tr><td style="padding:8px 10px;border:1px solid #e6dfd0">5% annual cap</td><td style="padding:8px 10px;border:1px solid #e6dfd0;color:#2f6f4f">recommended</td></tr>
     <tr><td style="padding:8px 10px;border:1px solid #e6dfd0">Hard 3% cap</td><td style="padding:8px 10px;border:1px solid #e6dfd0;color:#9a6b12">the other side may resist</td></tr>
     <tr><td style="padding:8px 10px;border:1px solid #e6dfd0">No cap</td><td style="padding:8px 10px;border:1px solid #e6dfd0;color:#9a3412">unfavourable</td></tr>
    </table>
  </td></tr>
  <tr><td style="padding:10px 30px 28px 30px">
    <div style="font-size:14px;margin-top:18px">Yours faithfully,<br><span style="font-family:'Brush Script MT',cursive;font-size:22px;color:#3b3428">Jacek Wrona</span><br><span style="font-family:Arial,sans-serif;font-size:12px;color:#8c8271">attorney at law · bar no. WA-8821</span></div>
  </td></tr>
  <tr><td style="background:#faf7f0;border-top:1px solid #e6dfd0;padding:14px 30px;font-family:Arial,sans-serif;font-size:10px;line-height:1.7;color:#8c8271">
   Wrona Law Office · 12 Marszałkowska St, 00-590 Warsaw · This opinion is prepared solely for the addressee and may not be shared with third parties without the firm's consent.
  </td></tr>
 </table>
</div>` },

  proposedTerms: { title: "Proposed terms 2027", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="620" align="center" cellpadding="0" cellspacing="0" style="width:620px;max-width:100%;background:#ffffff;border:1px solid #d7dee7">
  <tr><td style="background:#14293f;padding:20px 26px;color:#ffffff">
    <table width="100%"><tr><td style="font-size:17px;letter-spacing:.06em">CONTOSO</td><td align="right" style="font-size:12px;color:#9fb3c8">Proposal no. 2027/PL-04</td></tr></table>
  </td></tr>
  <tr><td style="padding:22px 26px 6px 26px">
    <p style="font-size:14px;line-height:1.7;margin:0 0 16px 0;color:#3e4c59">Below are three options for 2027. Option B is our recommendation.</p>
    <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px">
     <tr style="background:#f2f5f9"><th align="left" style="padding:10px;border:1px solid #d7dee7">Option</th><th align="left" style="padding:10px;border:1px solid #d7dee7">SLA</th><th align="left" style="padding:10px;border:1px solid #d7dee7">Price / year</th><th align="left" style="padding:10px;border:1px solid #d7dee7">Indexation</th></tr>
     <tr><td style="padding:10px;border:1px solid #d7dee7">A · unchanged</td><td style="padding:10px;border:1px solid #d7dee7">4 h</td><td style="padding:10px;border:1px solid #d7dee7">€238,000</td><td style="padding:10px;border:1px solid #d7dee7">CPI uncapped</td></tr>
     <tr style="background:#eef5ff"><td style="padding:10px;border:1px solid #d7dee7"><strong>B · recommended</strong></td><td style="padding:10px;border:1px solid #d7dee7"><strong>2 h</strong></td><td style="padding:10px;border:1px solid #d7dee7"><strong>€257,040</strong></td><td style="padding:10px;border:1px solid #d7dee7">CPI, cap to be agreed</td></tr>
     <tr><td style="padding:10px;border:1px solid #d7dee7">C · extended</td><td style="padding:10px;border:1px solid #d7dee7">2 h + weekends</td><td style="padding:10px;border:1px solid #d7dee7">€289,000</td><td style="padding:10px;border:1px solid #d7dee7">CPI + 1 pp</td></tr>
    </table>
    <p style="font-size:12px;color:#7b8794;margin:14px 0 0 0">Prices net. The proposal is binding until <strong style="color:#9a3412">28.08.2026</strong>.</p>
    <div style="margin:18px 0 6px 0;padding:14px 16px;background:#f7f9fb;border:1px solid #e3e8ee">
     <div style="font-size:13px;font-weight:bold;color:#14293f;margin-bottom:6px">What changes versus 2026</div>
     <ul style="margin:0;padding-left:18px;font-size:13px;line-height:1.7;color:#3e4c59">
      <li>shorter response time for critical incidents</li>
      <li>dedicated technical account manager (2 days a month)</li>
      <li>availability report monthly instead of quarterly</li>
     </ul>
    </div>
  </td></tr>
  <tr><td style="padding:12px 26px 24px 26px">
   <table cellpadding="0" cellspacing="0"><tr>
    <td style="background:#14293f"><a href="#" style="display:inline-block;padding:11px 20px;color:#ffffff;text-decoration:none;font-size:13px">Accept option B</a></td>
    <td style="padding-left:10px"><a href="#" style="display:inline-block;padding:11px 20px;color:#14293f;text-decoration:none;font-size:13px;border:1px solid #c3ccd8">Book a call</a></td>
   </tr></table>
  </td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:14px 26px;font-size:11px;color:#9aa5b1;line-height:1.6">Anna Kowalska · Key Account Manager · +48 22 000 00 00<br>Contoso Ltd, 51 Prosta St, 00-838 Warsaw</td></tr>
 </table>
</div>` },

  satisfactionSurvey: { title: "Satisfaction survey 2026", html: `
<div style="padding:24px 12px;font-family:'Segoe UI',Arial,Helvetica,sans-serif;background:#eef1f5">
 <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border-radius:10px;overflow:hidden">
  <tr><td style="background:#4b3f8f;padding:30px 28px;text-align:center;color:#ffffff">
    <div style="font-size:12px;letter-spacing:.18em;opacity:.8">PEOPLE TEAM</div>
    <div style="font-size:26px;margin-top:8px">How is work going for you?</div>
    <div style="font-size:13px;opacity:.85;margin-top:8px">7 minutes · answers fully anonymous</div>
  </td></tr>
  <tr><td style="padding:26px 28px 10px 28px">
   <p style="font-size:14px;line-height:1.7;color:#3e4c59;margin:0 0 18px 0">Once a year we ask the same questions so the results can be compared. Last year 78% of the team responded — that is where the training budget and flexible start hours came from.</p>
   <div style="text-align:center;padding:6px 0 14px 0">
    <div style="font-size:13px;color:#6b7280;margin-bottom:10px">Start with one click — how would you rate the last quarter?</div>
    <table align="center" cellpadding="0" cellspacing="0"><tr>
     <td style="padding:0 4px"><a href="#" style="display:inline-block;width:38px;height:38px;line-height:38px;border-radius:19px;background:#f3eefc;color:#4b3f8f;text-decoration:none;font-size:15px">1</a></td>
     <td style="padding:0 4px"><a href="#" style="display:inline-block;width:38px;height:38px;line-height:38px;border-radius:19px;background:#f3eefc;color:#4b3f8f;text-decoration:none;font-size:15px">2</a></td>
     <td style="padding:0 4px"><a href="#" style="display:inline-block;width:38px;height:38px;line-height:38px;border-radius:19px;background:#f3eefc;color:#4b3f8f;text-decoration:none;font-size:15px">3</a></td>
     <td style="padding:0 4px"><a href="#" style="display:inline-block;width:38px;height:38px;line-height:38px;border-radius:19px;background:#4b3f8f;color:#ffffff;text-decoration:none;font-size:15px">4</a></td>
     <td style="padding:0 4px"><a href="#" style="display:inline-block;width:38px;height:38px;line-height:38px;border-radius:19px;background:#f3eefc;color:#4b3f8f;text-decoration:none;font-size:15px">5</a></td>
    </tr></table>
   </div>
   <table width="100%" style="border-collapse:collapse;font-size:13px;color:#3e4c59">
    <tr><td style="padding:8px 0;border-top:1px solid #eef1f5">Areas covered</td><td align="right" style="padding:8px 0;border-top:1px solid #eef1f5">6</td></tr>
    <tr><td style="padding:8px 0;border-top:1px solid #eef1f5">Open questions</td><td align="right" style="padding:8px 0;border-top:1px solid #eef1f5">2</td></tr>
    <tr><td style="padding:8px 0;border-top:1px solid #eef1f5">Deadline</td><td align="right" style="padding:8px 0;border-top:1px solid #eef1f5;color:#9a3412"><strong>5 September</strong></td></tr>
   </table>
  </td></tr>
  <tr><td style="padding:8px 28px 28px 28px;text-align:center"><a href="#" style="display:inline-block;background:#4b3f8f;color:#ffffff;text-decoration:none;padding:14px 34px;border-radius:24px;font-size:14px">Take the survey</a></td></tr>
  <tr><td style="background:#f7f9fb;padding:16px 28px;font-size:11px;color:#9aa5b1;line-height:1.6;text-align:center">An external platform collects the answers; we only see aggregate results.<br><a href="#" style="color:#4b3f8f">Data processing policy</a> · <a href="#" style="color:#4b3f8f">no reminders, please</a></td></tr>
 </table>
</div>` },

  reportingQuote: { title: "Quote — reporting module", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="620" align="center" cellpadding="0" cellspacing="0" style="width:620px;max-width:100%;background:#ffffff;border:1px solid #d7dee7">
  <tr><td style="padding:22px 26px;border-bottom:4px solid #b4531f">
    <table width="100%"><tr>
     <td style="font-size:19px;color:#7a3a15;letter-spacing:.04em"><strong>NORTHWIND</strong></td>
     <td align="right" style="font-size:12px;color:#7b8794">Quote OF/2026/311<br>valid to 30.09.2026</td>
    </tr></table>
  </td></tr>
  <tr><td style="padding:22px 26px 6px 26px">
   <h1 style="font-size:18px;margin:0 0 10px 0;color:#14293f">Reporting module — implementation and support</h1>
   <p style="font-size:14px;line-height:1.7;color:#3e4c59;margin:0 0 16px 0">The scope covers 12 standard reports, a custom report builder and export to XLSX and PDF. We deliver in three two-week iterations.</p>
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px">
    <tr style="background:#fdf3ec"><th align="left" style="padding:9px 10px;border:1px solid #ecd9cc">Item</th><th align="right" style="padding:9px 10px;border:1px solid #ecd9cc">Qty</th><th align="right" style="padding:9px 10px;border:1px solid #ecd9cc">Price</th></tr>
    <tr><td style="padding:9px 10px;border:1px solid #ecd9cc">Module licence (perpetual)</td><td align="right" style="padding:9px 10px;border:1px solid #ecd9cc">1</td><td align="right" style="padding:9px 10px;border:1px solid #ecd9cc">€14,000</td></tr>
    <tr><td style="padding:9px 10px;border:1px solid #ecd9cc">Implementation and configuration</td><td align="right" style="padding:9px 10px;border:1px solid #ecd9cc">60 h</td><td align="right" style="padding:9px 10px;border:1px solid #ecd9cc">€8,400</td></tr>
    <tr><td style="padding:9px 10px;border:1px solid #ecd9cc">Team training</td><td align="right" style="padding:9px 10px;border:1px solid #ecd9cc">1 day</td><td align="right" style="padding:9px 10px;border:1px solid #ecd9cc">€1,600</td></tr>
    <tr style="background:#fdf3ec"><td colspan="2" style="padding:11px 10px;border:1px solid #ecd9cc"><strong>Total net</strong></td><td align="right" style="padding:11px 10px;border:1px solid #ecd9cc"><strong>€24,000</strong></td></tr>
   </table>
   <div style="margin:18px 0 6px 0;padding:13px 15px;background:#f7f9fb;border-left:3px solid #b4531f;font-size:13px;line-height:1.6;color:#3e4c59">
    Schedule: start two weeks after signature, final acceptance after six weeks. Support €1,200/month from the second month after acceptance.
   </div>
  </td></tr>
  <tr><td style="padding:12px 26px 24px 26px"><a href="#" style="display:inline-block;background:#b4531f;color:#ffffff;text-decoration:none;padding:12px 22px;font-size:13px">Download the quote as PDF</a></td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:14px 26px;font-size:11px;color:#9aa5b1">Robert Lis · Northwind Ltd · r.lis@northwind.example · +48 61 000 00 00</td></tr>
 </table>
</div>` },

  deliveryDateChange: { title: "Q4 delivery date change", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="620" align="center" cellpadding="0" cellspacing="0" style="width:620px;max-width:100%;background:#ffffff;border:1px solid #d7dee7">
  <tr><td style="background:#5b3f2e;padding:18px 26px;color:#ffffff;font-size:16px;letter-spacing:.05em">FABRIKAM · LOGISTICS</td></tr>
  <tr><td style="padding:22px 26px 8px 26px">
   <div style="background:#fdf0e4;border:1px solid #ecd0ae;padding:14px 16px;font-size:14px;line-height:1.6;color:#7a4a15">
    <strong>Date change:</strong> the Q4 delivery moves from 28.10 to <strong>12.11.2026</strong>.
   </div>
   <table width="100%" cellpadding="0" cellspacing="0" style="margin-top:20px;border-collapse:collapse;font-size:13px">
    <tr><td style="padding:10px;border:1px solid #e3e8ee;width:34%;background:#f7f9fb">Order number</td><td style="padding:10px;border:1px solid #e3e8ee;font-family:monospace">ORD-2026-0914</td></tr>
    <tr><td style="padding:10px;border:1px solid #e3e8ee;background:#f7f9fb">Items</td><td style="padding:10px;border:1px solid #e3e8ee">4 pallets · 1,240 kg</td></tr>
    <tr><td style="padding:10px;border:1px solid #e3e8ee;background:#f7f9fb">Original date</td><td style="padding:10px;border:1px solid #e3e8ee;color:#8a94a6"><s>28.10.2026</s></td></tr>
    <tr><td style="padding:10px;border:1px solid #e3e8ee;background:#f7f9fb">New date</td><td style="padding:10px;border:1px solid #e3e8ee"><strong>12.11.2026, window 08:00–14:00</strong></td></tr>
    <tr><td style="padding:10px;border:1px solid #e3e8ee;background:#f7f9fb">Reason</td><td style="padding:10px;border:1px solid #e3e8ee">line stoppage at the component manufacturer</td></tr>
   </table>
   <p style="font-size:12px;color:#7b8794;margin:16px 0 6px 0">Fulfilment stages</p>
   <table width="100%" style="font-size:12px;color:#3e4c59;border-collapse:collapse">
    <tr><td style="padding:6px 0;width:24px;color:#2f6f4f">✔</td><td style="padding:6px 0">Order confirmed</td><td align="right">14.08</td></tr>
    <tr><td style="padding:6px 0;color:#2f6f4f">✔</td><td style="padding:6px 0">Component production</td><td align="right">02.09</td></tr>
    <tr><td style="padding:6px 0;color:#9a6b12">●</td><td style="padding:6px 0">Picking and inspection</td><td align="right">05.11</td></tr>
    <tr><td style="padding:6px 0;color:#9aa5b1">○</td><td style="padding:6px 0">Transport and unloading</td><td align="right">12.11</td></tr>
   </table>
   <div style="margin-top:18px;font-size:13px;line-height:1.6;color:#3e4c59">Please confirm the new delivery window <strong>by 02.09</strong>. Without confirmation we will book the fallback date of 19.11.</div>
  </td></tr>
  <tr><td style="padding:14px 26px 24px 26px">
   <table cellpadding="0" cellspacing="0"><tr>
    <td style="background:#5b3f2e"><a href="#" style="display:inline-block;padding:11px 20px;color:#ffffff;text-decoration:none;font-size:13px">Potwierdzam 12.11</a></td>
    <td style="padding-left:10px"><a href="#" style="display:inline-block;padding:11px 20px;color:#5b3f2e;text-decoration:none;font-size:13px;border:1px solid #d9c9bb">Propose another date</a></td>
   </tr></table>
  </td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:13px 26px;font-size:11px;color:#9aa5b1">Tomasz Bąk · Fabrikam · logistics · t.bak@fabrikam.example</td></tr>
 </table>
</div>` },

  auditRequest: { title: "Audit request — IT contract register", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="620" align="center" cellpadding="0" cellspacing="0" style="width:620px;max-width:100%;background:#ffffff;border:1px solid #d7dee7;border-top:5px solid #37474f">
  <tr><td style="padding:22px 26px 6px 26px">
   <div style="font-size:11px;letter-spacing:.14em;color:#7b8794">INTERNAL AUDIT · REQUEST NO. A-2026/17</div>
   <h1 style="font-size:19px;margin:8px 0 14px 0;color:#263238">IT contract register 2024–2026</h1>
   <p style="font-size:14px;line-height:1.7;color:#3e4c59;margin:0 0 16px 0">As part of the periodic review, please send a complete list of IT contracts including addenda. The required fields are listed below — please keep the column order.</p>
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:12px">
    <tr style="background:#eceff1"><th align="left" style="padding:8px 10px;border:1px solid #cfd8dc">Pole</th><th align="left" style="padding:8px 10px;border:1px solid #cfd8dc">Format</th><th align="left" style="padding:8px 10px;border:1px solid #cfd8dc">Wymagane</th></tr>
    <tr><td style="padding:8px 10px;border:1px solid #cfd8dc">Kontrahent / NIP</td><td style="padding:8px 10px;border:1px solid #cfd8dc">tekst</td><td style="padding:8px 10px;border:1px solid #cfd8dc">tak</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #cfd8dc">Signature date / term</td><td style="padding:8px 10px;border:1px solid #cfd8dc">YYYY-MM-DD</td><td style="padding:8px 10px;border:1px solid #cfd8dc">yes</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #cfd8dc">Annual value, net</td><td style="padding:8px 10px;border:1px solid #cfd8dc">EUR</td><td style="padding:8px 10px;border:1px solid #cfd8dc">yes</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #cfd8dc">Notice period</td><td style="padding:8px 10px;border:1px solid #cfd8dc">months</td><td style="padding:8px 10px;border:1px solid #cfd8dc">yes</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #cfd8dc">Data processing (GDPR)</td><td style="padding:8px 10px;border:1px solid #cfd8dc">yes / no</td><td style="padding:8px 10px;border:1px solid #cfd8dc">if applicable</td></tr>
   </table>
   <div style="margin:18px 0 6px 0;padding:13px 15px;background:#eceff1;font-size:13px;line-height:1.6;color:#37474f">
    <strong>Data due: 3 September 2026.</strong> Please send the list as XLSX to audit@company.example, copying the board office.
   </div>
   <p style="font-size:12px;color:#7b8794;line-height:1.7;margin:14px 0 0 0">Basis: 2026 audit plan, point 4.2. The request needs no formal reply; sending the list is enough.</p>
  </td></tr>
  <tr><td style="padding:14px 26px 24px 26px"><a href="#" style="display:inline-block;background:#37474f;color:#ffffff;text-decoration:none;padding:11px 20px;font-size:13px">Download the template XLSX</a></td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:13px 26px;font-size:11px;color:#9aa5b1">Internal Audit · ext. 412 · correspondence is archived</td></tr>
 </table>
</div>` },

  ndaDraft: { title: "Projekt NDA — Northwind", html: `
<div style="padding:24px 12px;font-family:Georgia,'Times New Roman',serif">
 <table role="presentation" width="640" align="center" cellpadding="0" cellspacing="0" style="width:640px;max-width:100%;background:#ffffff;border:1px solid #d9d2c5">
  <tr><td style="padding:24px 30px 10px 30px;border-bottom:1px solid #e6dfd0">
   <div style="font-family:Arial,sans-serif;font-size:11px;letter-spacing:.12em;color:#8c8271">KANCELARIA WRONA · PROJEKT DOKUMENTU</div>
   <h1 style="font-size:19px;margin:10px 0 4px 0;color:#3b3428">Non-disclosure agreement (NDA)</h1>
   <div style="font-family:Arial,sans-serif;font-size:12px;color:#8c8271">version 2 · changes marked in colour</div>
  </td></tr>
  <tr><td style="padding:20px 30px 6px 30px">
   <p style="font-size:15px;line-height:1.75;margin:0 0 12px 0"><strong>§ 3 Term.</strong> The confidentiality obligation binds the parties for <span style="background:#fff3bf">three years</span> from the end of the engagement.</p>
   <p style="font-size:15px;line-height:1.75;margin:0 0 12px 0"><strong>§ 5 Contractual penalty.</strong> In the event of a breach, the obliged party pays a penalty of <s style="color:#a1887f">€100,000</s> <span style="background:#e6f4ea;color:#1b5e20">€50,000</span> per breach.</p>
   <p style="font-size:15px;line-height:1.75;margin:0 0 12px 0"><strong>§ 7 Carve-outs.</strong> Confidentiality does not cover publicly available information or disclosures required by a public authority.</p>
   <table width="100%" cellpadding="0" cellspacing="0" style="margin:18px 0 8px 0;border-collapse:collapse;font-family:Arial,sans-serif;font-size:12px">
    <tr style="background:#faf7f0"><th align="left" style="padding:8px 10px;border:1px solid #e6dfd0">Zmiana</th><th align="left" style="padding:8px 10px;border:1px solid #e6dfd0">Wersja 1</th><th align="left" style="padding:8px 10px;border:1px solid #e6dfd0">Wersja 2</th></tr>
    <tr><td style="padding:8px 10px;border:1px solid #e6dfd0">Contractual penalty</td><td style="padding:8px 10px;border:1px solid #e6dfd0">€100,000</td><td style="padding:8px 10px;border:1px solid #e6dfd0;background:#e6f4ea">€50,000</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #e6dfd0">Okres</td><td style="padding:8px 10px;border:1px solid #e6dfd0">5 lat</td><td style="padding:8px 10px;border:1px solid #e6dfd0;background:#e6f4ea">3 lata</td></tr>
    <tr><td style="padding:8px 10px;border:1px solid #e6dfd0">Governing law</td><td style="padding:8px 10px;border:1px solid #e6dfd0">Polish</td><td style="padding:8px 10px;border:1px solid #e6dfd0">unchanged</td></tr>
   </table>
   <div style="border-left:3px solid #b9ae97;background:#faf7f0;padding:13px 16px;margin:16px 0;font-family:Arial,sans-serif;font-size:13px;line-height:1.65;color:#5d5545">
    For signature <strong>by 5 September</strong>. If the other side rejects the lower penalty, I recommend the variant with a €150,000 aggregate cap.
   </div>
  </td></tr>
  <tr><td style="padding:8px 30px 26px 30px"><a href="#" style="display:inline-block;background:#3b3428;color:#ffffff;text-decoration:none;padding:11px 22px;font-family:Arial,sans-serif;font-size:13px">Sign electronically</a></td></tr>
  <tr><td style="background:#faf7f0;border-top:1px solid #e6dfd0;padding:14px 30px;font-family:Arial,sans-serif;font-size:10px;color:#8c8271;line-height:1.7">Working document · not an offer in the legal sense · Wrona Law Office</td></tr>
 </table>
</div>` },

  weekendSla: { title: "Question about weekend SLA", html: `
<div style="padding:24px 12px;font-family:Arial,Helvetica,sans-serif">
 <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border:1px solid #dde3ea">
  <tr><td style="padding:22px 26px 4px 26px">
   <p style="font-size:15px;line-height:1.7;color:#2c3a47;margin:0 0 14px 0">Hello,</p>
   <p style="font-size:15px;line-height:1.7;color:#2c3a47;margin:0 0 14px 0">coming back to weekend support. From October we are starting Saturday cover for three of our clients and we need to know whether the current contract covers it.</p>
   <table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;font-size:13px;margin:6px 0 14px 0">
    <tr style="background:#f2f5f9"><th align="left" style="padding:9px 10px;border:1px solid #dde3ea">Parameter</th><th align="left" style="padding:9px 10px;border:1px solid #dde3ea">Today</th><th align="left" style="padding:9px 10px;border:1px solid #dde3ea">Expected</th></tr>
    <tr><td style="padding:9px 10px;border:1px solid #dde3ea">Days covered</td><td style="padding:9px 10px;border:1px solid #dde3ea">Mon–Fri</td><td style="padding:9px 10px;border:1px solid #dde3ea">Mon–Sat</td></tr>
    <tr><td style="padding:9px 10px;border:1px solid #dde3ea">Response time (critical)</td><td style="padding:9px 10px;border:1px solid #dde3ea">2 h</td><td style="padding:9px 10px;border:1px solid #dde3ea">4 h on Saturday</td></tr>
    <tr><td style="padding:9px 10px;border:1px solid #dde3ea">Ticket channel</td><td style="padding:9px 10px;border:1px solid #dde3ea">portal</td><td style="padding:9px 10px;border:1px solid #dde3ea">portal + on-call phone</td></tr>
   </table>
   <p style="font-size:15px;line-height:1.7;color:#2c3a47;margin:0 0 14px 0">If there is a surcharge, please give us a range — we take the budget decision next week.</p>
   <div style="border-left:3px solid #c3ccd8;padding:10px 14px;margin:18px 0 6px 0;color:#7b8794;font-size:13px;line-height:1.6">
    <div style="font-size:11px;text-transform:uppercase;letter-spacing:.08em;margin-bottom:6px">Quoted earlier message</div>
    On 26.08.2026 Karolina Kowalska wrote:<br>“The current support scope covers business days 8:00–18:00. An extension is possible and requires an addendum.”
   </div>
  </td></tr>
  <tr><td style="padding:12px 26px 22px 26px">
   <table width="100%" style="border-top:1px solid #eef1f5;padding-top:12px"><tr><td style="padding-top:14px;font-size:12px;line-height:1.7;color:#5b6672">
     <strong style="color:#14293f;font-size:13px">Ewa Sikora</strong><br>
     Head of Support · Adventure Works<br>
     <span style="color:#9aa5b1">tel. +48 58 000 00 00 · e.sikora@adventure.example</span>
   </td></tr></table>
  </td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:12px 26px;font-size:10px;color:#9aa5b1;line-height:1.6">Adventure Works Ltd · This message may contain confidential information. If you are not the addressee, delete it and notify the sender.</td></tr>
 </table>
</div>` },
};
