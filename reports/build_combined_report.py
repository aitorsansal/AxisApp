from pathlib import Path
from html.parser import HTMLParser
class N:
 def __init__(self,tag='',attrs=()): self.tag=tag;self.a=dict(attrs);self.children=[]
 def text(self): return ' '.join(c.text() if isinstance(c,N) else c.strip() for c in self.children).strip()
 def find(self,tag=None,cls=None):
  out=[]
  for c in self.children:
   if isinstance(c,N):
    if (tag is None or c.tag==tag) and (cls is None or cls in c.a.get('class','').split()):out.append(c)
    out+=c.find(tag,cls)
  return out
class Parser(HTMLParser):
 def __init__(self):super().__init__();self.root=N();self.stack=[self.root]
 def handle_starttag(self,t,a):
  n=N(t,a);self.stack[-1].children.append(n)
  if t not in ('img','br','hr','meta','input','link','source','wbr'):self.stack.append(n)
 def handle_endtag(self,t):
  for i in range(len(self.stack)-1,0,-1):
   if self.stack[i].tag==t:self.stack=self.stack[:i];break
 def handle_data(self,d):self.stack[-1].children.append(d)
def parse(name):
 p=Parser();p.feed((ROOT/name).read_text(encoding='utf-8'));return p.root
ROOT=Path(__file__).resolve().parent
claude=parse('REPORT_CLAUDE.html');codex=parse('REPORT_CODEX.html')
C={};F={}
for n in claude.find('article','finding'):
 key=n.find(cls='id')[0].text(); C[key]=n
for n in codex.find('article','finding'):F[n.find(cls='issue-id')[0].text()]=n
# Metadata is editorial synthesis. Original observations are retained separately.
rows=[
('A1','Entering expenses','first','A decimal point can turn 8.50 into 850','On Android in Spanish, Claude entered 8.50 and the field became 850. Comma input worked in both sessions.','Retest typed and pasted dot/comma amounts on the actual device. The suspected native input-filter cause is unverified.'),
('W1','Events & transport','first','An invalid event date can freeze Save','A malformed date reportedly throws an error and leaves the event form stuck on Saving.','Reject impossible dates before submission and leave Save usable after any failure.'),
('W2','Entering expenses','first','An impossible expense date is saved in another month','Claude entered 31 February; the expense was saved as 3 March and appeared in the wrong month in statistics.','Reject impossible dates. Verify that valid dates survive save and reopen unchanged.'),
('W4 / A2','Repayments','first','A tap records a full repayment immediately','Both saw full repayment without an amount preview. Claude also settled two people with consecutive taps as rows moved.','Review the person and amount before recording. Support partial repayment and prevent taps from hitting the next row.'),
('W3','Events & transport','first','Calendar exports contain malformed timestamps','Claude reports calendar timestamps containing both a timezone offset and Z. Strict calendar apps may reject them.','Validate exported timestamps and import into a real calendar. Verify explicit and fallback end times.'),
('W10','Editing expenses','first','Equal splits reopen as Manual','Both reproduced this on web and Android. Changing the total leaves old shares, so Save fails until Equal is selected again.','Preserve split intent and recalculate equal shares. Do not infer intent solely because manual shares happen to be equal.'),
('W5 / A3','Entering expenses','next','Some web categories have no Android label','Rent and utilities can be selected on web, but Android displays raw keys or generic styling. The saved category is preserved.','Agree on shared categories and support existing data. Verify labels, icons and charts on both clients.'),
('W6','Members & invites','next','A group can be named with only spaces','Claude created a group whose title and sidebar entry appeared empty.','Trim names and reject empty results at both client and data boundaries.'),
('W7 / A8','Repayments','next','Repayment editing uses the expense split form','A repayment should have one recipient, but the editor allows expense-style choices that lead to errors or unexplained disabled Save.','Use one sender, one recipient, amount and date. Explain invalid states.'),
('W8 / A7','Repayments','next','Repayment descriptions differ between clients','A custom description appears on Android but web shows a generic label. Automatic labels can retain the creator’s language.','Show custom text consistently and localize automatic labels when displayed.'),
('A5','Stats & activity','next','A new expense can look 14 hours old','Both saw misleading relative times: new expenses looked old while older repayments appeared to be from right now.','Distinguish when money was spent from when it was entered. Prefer Today for date-only records.'),
('A4','Entering expenses','polish','Every Android expense uses the cart icon','Transport, food and rent rows all use the same shopping-cart icon.','Use category-specific icons with a sensible fallback.'),
('A6','Events & transport','next','Back from an event can open the wrong group','An Android link opens the correct event, but Back returns to a previously open, unrelated group.','Build a navigation path that returns to the event’s actual group.'),
('W11','Events & transport','next','Saving an event sends you to Expenses','Both saw a saved event disappear from view because web returns to the Expenses tab. Claude also reports resets after Back or reload.','Retain Events context after creation, navigation and reload.'),
('W12','Entering expenses','next','Spanish screens show raw English errors','Receipt, amount, invitation and repayment failures expose browser/server messages in English.','Translate known failures and provide a useful localized fallback.'),
('W13','Entering expenses','next','Scrolling can silently change an amount','Claude reports 25 changing to 24.99 when scrolling over a focused amount field. Decimal display also varies.','Prevent wheel-driven amount changes and test locale-aware input and display.'),
('W14','Entering expenses','next','Oversized amounts reach a database error','Very large input produces numeric field overflow; share fields can truncate digits.','Define supported limits and show a clear validation error before saving.'),
('W16','Editing expenses','next','Members cannot inspect an expense’s change history','Both identify missing visible history. Claude reports stored history already exists; Codex only establishes that controls were not found.','Expose who changed what and when. Verify stored-history claims before implementation.'),
('W15','Events & transport','polish','Desktop sharing needs an obvious Copy link option','The event Share button opens the Windows share sheet. Claude wanted a direct copy-link alternative.','Offer Copy link alongside sharing. Native sharing itself is a product choice, not inherently a defect.'),
('W17','Members & invites','next','Adding a guest unexpectedly replaces the clipboard','Both saw named-member creation also generate an invitation and copy its link. Claude additionally reports older resend tokens stay valid.','Make copying explicit. Review token lifecycle separately; that extension was only reported by Claude.'),
('W18','Members & invites','next','Joining does not preview the group or identity','The join screen shows a code but does not explain the group or phantom identity being claimed.','Show what joining will do before confirmation.'),
('A9','Stats & activity','polish','Android charts clip values and crowd labels','Axis labels overlap, values clip at the edge, and a line connects months across a long gap.','Fix spacing and ticks. Decide whether missing months mean zero spending or unavailable data.'),
('A10','Profile & appearance','polish','Names and avatar initials disagree','Both saw stale CT initials while other areas showed CQ. Claude also notes email display and indistinguishable CQ avatars.','Refresh identity consistently and make different members distinguishable.'),
('A11','Profile & appearance','polish','Spanish money formatting differs by client','Android and web place currency symbols differently for Spanish.','Use a consistent locale-aware currency convention; separate display from arithmetic.'),
('W19','Profile & appearance','next','Profile feedback can be hidden or stale','Claude saw off-screen save feedback and stale sidebar identity. Codex saw name propagation in its sequence.','Retest the exact save/navigation sequence. Keep feedback visible and update identity everywhere.'),
('W20','Events & transport','polish','Event defaults can produce confusing starting states','The start defaults to the current minute, the end date does not follow it, and a car offer can start with zero seats.','Choose useful date defaults and explain whether a zero-seat offer is intentional.'),
('W23','Entering expenses','polish','Receipt previews crop text and picks upload early','Claude reports clipped receipt previews and uploads that can remain after abandoning a form.','Separate preview layout from upload lifecycle; verify orphan cleanup before changing behavior.'),
('A12','Stats & activity','polish','Empty sections do not explain what is missing','Android can show an empty Balances or Going heading with no explanation.','Add useful empty-state copy.'),
]
merges={'W4 / A2':'F07','W10':'F01','A5':'F09','W11':'F06','W17':'F10'}
extras=[
('F02','Events & transport','first','An open web view can show seats that no longer exist','Codex changed Going / three seats to Maybe / zero seats on Android. Web stayed stale until reload; Claude saw instant updates in its session.','Retest the same open view repeatedly with timestamps. Successful persistence does not establish reliable live sync.'),
('F03','Editing expenses','first','Navigation silently discards an unsaved edit','Codex changed an expense description, opened Profile, and returned to find the edit gone without a warning.','Offer an explicit discard choice or retain a recoverable draft.'),
('F04','Events & transport','next','Android event expenses exclude non-attendees','Only Going attendees appear as payers and participants. Web permits all group members and preselects attendees.','Agree whether non-attendees can pay or share, then align both clients.'),
('F05','Accessibility','next','Android actions and chart values lack readable names','Hierarchy inspection found private-use icon text without descriptions, and visual chart values absent from accessible text.','Add action names and accessible chart values; verify with TalkBack. No full screen-reader audit was performed.'),
('F08','Entering expenses','next','Currency conversions are not explained','The tested web form shows converted totals without a visible rate, date or source. Neither report establishes that the rate is wrong.','Show the applied conversion. Treat an actual-bank-charge override as a separate product decision.'),
]
polish=[
('Members & invites','Enter does not add a named member','The named-member input does not submit with Enter. Fixed 30 October 2026: Enter adds the member on web and Android, gated like the Add button.','Use standard form submission and verify keyboard behavior.'),
('Entering expenses','Other is not the last category','Legacy rent and utilities appear after Other.','Review ordering alongside the category compatibility issue.'),
('Accessibility','Web form controls have weak accessible labels','Checkboxes, date fields and share fields are insufficiently named.','Associate visible labels and verify keyboard and screen-reader output.'),
('Stats & activity','Dates look different inside event expenses','Event-linked rows use a numeric date while surrounding screens use a short month label.','Use a consistent display format. This is separate from invalid-date saving.'),
('Profile & appearance','The green balance theme has weak contrast','The gradient ends in mauve and dark balance text can be difficult to read.','Measure contrast and review the green theme.'),
('Profile & appearance','A default display name can expose the email','New members may be identified to co-members by their email address.','Decide whether to request a name or use another default. Unauthorized access was not established.'),
('Entering expenses','Your own payments use your name in the sentence','The payer line says the user’s name rather than You paid.','Choose consistent personal wording; this is copy preference.'),
('Members & invites','Confirmation dialogs use different styles','Delete, leave, dissolve, rename and account deletion use native dialogs rather than consistent in-app flows.','Review safeguards per action; a native confirmation is not automatically unsafe.'),
('Entering expenses','Invalid negative amounts still show negative shares','Save is correctly blocked, but the split preview displays negative values.','Keep invalid previews clear while preserving save validation.'),
('Events & transport','An event location has no map link','Users cannot open directions directly from the displayed location.','Consider an explicit map action.'),
('Events & transport','Event expenses do not suggest the event title','Adding an expense from an event starts without the event title as a suggested description.','Consider a useful editable default.'),
('Repayments','Repayment terminology changes between screens','Spanish labels mix Saldar, Liquidar and liquidación.','Choose consistent terminology across both clients.'),
('Members & invites','Renaming a member is hard to discover','Web requires clicking the member’s name without a visible edit affordance.','Expose the alias edit action as Android does with its pencil.'),
]
def original(n,author,ref):
 parts=[]
 if author=='Claude':
  dts=n.find('dt');dds=n.find('dd')
  parts=[{'label':a.text(),'text':b.text()} for a,b in zip(dts,dds)]
 else:
  for x in n.find('ol'):parts.append({'label':'Reproduce','text':'\n'.join(str(i+1)+'. '+v.text() for i,v in enumerate(x.find('li')))})
  for x in n.find('div','comparison'):
   for child in x.children:
    if isinstance(child,N):
     hs=child.find('h4');ps=child.find('p')
     if hs and ps:parts.append({'label':hs[0].text(),'text':ps[0].text()})
 return {'author':author,'ref':ref,'parts':parts,'priority':n.a.get('data-priority') or n.a.get('data-sev'),'images':[{'src':x.a['src'],'alt':x.a.get('alt','Reported screenshot')} for x in n.find('img')]}
issues=[]
for key,journey,priority,title,summary,verify in rows+extras:
 is_c=key in C;n=C[key] if is_c else F[key]
 sources=[original(n,'Claude' if is_c else 'Codex',key)]
 if key in merges:sources.append(original(F[merges[key]],'Codex',merges[key]))
 cross=key in ('A10','W16')
 platform=n.a.get('data-platform',n.a.get('data-plat'))
 platform={'both':'Both','web':'Web','android':'Android'}.get(platform,platform)
 if key=='W3':platform='Calendar'
 if key=='A5':platform='Android'
 agreement='Both observed' if len(sources)>1 or cross else ('Mixed observations' if key in ('F02','W19') else ('Claude only' if is_c else 'Codex only'))
 if key=='W16':sources.append({'author':'Codex','ref':'Feature opportunity: history','parts':[{'label':'Scope','text':'Visible expense history and comments were not found in the tested editor. Storage implementation was not inspected.'}],'priority':'Opportunity','images':[]})
 if key=='A10':sources.append({'author':'Codex','ref':'Scope and confidence: secondary polish','parts':[{'label':'Observed','text':'Android top-level initials CT versus CQ in the profile.'}],'priority':'Secondary','images':[]})
 if key in ('F02','W19'): sources.append({'author':'Claude' if key=='F02' else 'Codex','ref':'Verified working: cross-client sync' if key=='F02' else 'Profile synchronization','parts':[{'label':'Different observation','text':'Android expense and RSVP visible on web instantly.' if key=='F02' else 'Saved display name propagated to the sidebar and payer in the tested sequence.'}],'priority':'Pass','images':[]})
 issues.append(dict(id='R'+str(len(issues)+1).zfill(2),key=key,journey=journey,priority=priority,title=title,summary=summary,verify=verify,platform=platform,agreement=agreement,sources=sources))
for journey,title,summary,verify in polish:
 sources=[{'author':'Claude','ref':'W21–32 bundle','parts':[{'label':'Observed','text':summary}],'priority':'Low','images':[]}]
 if title.startswith('Dates look'):sources.append({'author':'Codex','ref':'Review notes / secondary polish','parts':[{'label':'Observed','text':'Inconsistent date formats in event expenses and surrounding screens.'}],'priority':'Secondary','images':[]})
 issues.append(dict(id='R'+str(len(issues)+1).zfill(2),key='W21–32',journey=journey,priority='next' if journey=='Accessibility' else 'polish',title=title,summary=summary,verify=verify,platform='Web',agreement='Both observed' if len(sources)>1 else 'Claude only',sources=sources))
# Claude evidence lives outside its finding cards; attach captions explicitly.
for fig in claude.find('figure'):
 caps=fig.find('figcaption');imgs=fig.find('img')
 if not caps or not imgs:continue
 caption=caps[0].text()
 for issue in issues:
  if (issue['key']=='A1' and caption.startswith('A1:')) or (issue['key']=='W4 / A2' and caption.startswith('A2:')) or (issue['key'] in ('W5 / A3','A9') and caption.startswith('A3/')) or (issue['key']=='W7 / A8' and caption.startswith('A8:')):
   issue['sources'][0]['images']=[{'src':x.a['src'],'alt':caption} for x in imgs]
import json
web_review=json.loads((ROOT/'WEB_USER_REVIEW_DATA.json').read_text(encoding='utf-8'))
def web_source(item):
 return {'author':'Codex web user review','ref':item['ref'] if 'ref' in item else item['key'],'url':web_review['source'],'session':web_review['session'],'priority':item.get('result',item.get('status')),'parts':[{'label':item.get('result',item.get('status')),'text':item.get('observed',item.get('summary'))}],'images':[{'src':'web-user-review-2026-10-27/'+im['src'],'alt':im['alt']} for im in item.get('images',[])]}
for retest in web_review['retests']:
 issue=next(x for x in issues if x['key']==retest['key'])
 issue['sources'].append(web_source(retest))
 issue['status']=retest['result']
for addition in web_review['additions']:
 issues.append(dict(id='R'+str(len(issues)+1).zfill(2),key=addition['key'],journey=addition['journey'],priority=addition['priority'],title=addition['title'],summary=addition['summary'],verify=addition['verify'],platform='Web',agreement='Web user review',status=addition['status'],sources=[web_source(addition)]))
order={'U01':-1,'A1':0,'W1':1,'W2':2,'W4 / A2':3,'W3':4,'F02':5,'F03':6,'W10':7}
# Current implementation status supersedes historical reproduction badges.
status_review={
 'F03':('Expense editors protected on web and Android','Web expense/recurring protection was checked locally with isolated APIs and remains undeployed. Android 1.1.0 (10) now uses native discard confirmation and was verified on the connected phone for Cancel, system Back, Keep/Discard, reverted edits, incomplete new entries, successful saves and recurring schedule changes. Event/repayment forms and draft recovery remain outside scope. See UNSAVED_WEB_REVIEW.md and ANDROID_UNSAVED_REVIEW.md for evidence and limits.'),
 'F02':('Implemented on web; user-confirmed locally','The web had no live updates: event screens fetched attendees once on mount and after the viewer\'s own RSVP. EventDetailPage and GroupEventsTab now use useLiveRefresh: Supabase Realtime on events and event_attendees (applied to the live project with realtime_event_attendees.sql), plus refetch on tab focus, reconnect and a 60-second visible poll, skipped while the viewer\'s own RSVP or seat writes are pending. The user confirmed an open web view updates after a change from another client. Web production deployment, repeated timed runs and other screens (expenses, balances) were not checked.'),
 'W3':('Deployed; live feed verified','Calendar timestamps now normalize to UTC before formatting. Seven focused tests pass, including explicit ends, fallback ends, offsets, DST and parser import. Supabase deployed the fix on 28 October; the live QA feed returned three events with nine correctly formatted UTC timestamps and imported successfully through ICAL.js. No Google/Apple/Outlook subscription refresh was tested. See CALENDAR_FEED_REVIEW.md.'),
 'W10':('Implemented; Android device QA passed','The user confirmed SQL rollout and web save/reopen. Android 1.1.0 (10) now saves/restores split mode for expenses and recurring templates. Physical checks verified Equal reopen, total and participant recalculation, explicit Manual with equal-looking shares, and paused recurring editing. Legacy records remain Manual. See ANDROID_SPLIT_MODE_REVIEW.md for retained QA data and limits; alternating one record across both clients was not tested.'),
 'W1':('Implemented; local browser checked','The shared date field now rejects impossible and incomplete dates with localized inline errors. Event start/end dates and local times are validated before entering Saving; try/finally releases Saving after failures. Local browser checks covered invalid start/end dates, a valid leap-day correction and the existing end-after-start rule. Production deployment and a fresh database save/reopen were not checked.'),
 'W2':('Implemented; local browser checked','The shared calendar validator now guards ordinary and recurring expense submissions. expenseTimestamp rejects rollover dates instead of converting them to another month, while unchanged valid timestamps remain preserved. Local browser checks and 18 regression tests cover the date rules. Production deployment and a fresh valid-record save/reopen were not checked.'),
 'A1':('Implemented; prior device QA passed','Dot and comma input now share strict parsing on both clients. Android uses DecimalInput without stripping separators. The recorded device session saved and reopened EUR8.50 correctly and rejected grouped clipboard input. Both regression suites passed again on 28 October. See AMOUNT_INPUT_REVIEW.md; production web deployment was not checked in this status review.'),
 'W4 / A2':('Implemented; prior device QA passed','Both clients now open a repayment review before writing, support partial amounts and guard submission. Android QA recorded full/partial repayments, cancelled without writing, and restored temporary test records. Current domain tests pass. The earlier production reproduction predates this implementation; production web deployment was not checked today.'),
 'W7 / A8':('Implemented; prior device QA passed','Repayments route to dedicated editors with one payer, one distinct recipient, amount, date and note, rather than expense split controls. Android QA exercised editing, reversing direction and validation. Current settlement regressions pass; no fresh browser/device session was run today.'),
 'W8 / A7':('Partially implemented','Web group activity now displays saved repayment descriptions, matching Android. Custom notes are retained by the editors. Web event activity still substitutes a generic settlement label. Automatic descriptions remain stored in the creator language, so display-time localization is still open.'),
 'W13':('Partially implemented','Web amount and share fields now use text inputs with decimal inputMode, removing native number-spinner wheel changes. Prior browser QA checked that scrolling leaves the total unchanged. The broader cross-client display-format consistency claim has not been fully retested.'),
 'W14':('Implemented input limits; broader retest needed','Both parsers reject amounts above 9999999999.99 and excess precision before saving; manual shares retain invalid text and receive validation rather than truncation or rounding. Shared boundary fixtures pass in both suites. Converted amounts and every downstream database numeric constraint were not exhaustively audited.'),
}
release_status=json.loads((ROOT/'RELEASE_MONEY_STATUS.json').read_text(encoding='utf-8'))
status_review.update(release_status)
session_status=json.loads((ROOT/'INVITES_CURRENCY_STATUS.json').read_text(encoding='utf-8'))
status_review.update(session_status)
status_review['F02']=('Verified on web and Android','Repeated production web checks propagated RSVP and seats across event-detail and Events-list views without reload. Web now refetches on initial subscription/window focus and rejects stale read results after local taps. Simulated reconnect/focus/pending-write tests and 25 web regressions pass. Android polls (60-90 s delay by design, no Realtime) plus resume/reconnect refresh. On the physical phone, a web change appeared on the open event screen after about a minute and on app resume, and a phone change appeared instantly on web. The Wi-Fi off/on refresh did not visibly trigger. Same-row concurrent writes remain last-write-wins. See LIVE_ATTENDANCE_REVIEW.md for timings, deployment and limits.')
status_review['F03']=('Verified on web and Android','Web event creation/editing and repayment creation/editing now share the existing navigation/unload protection. Repayment Cancel/Escape/backdrop closure also requests discard; pending saves block leaving and successful writes bypass prompts. Isolated browser failure/delay/Keep/Discard checks pass in English/Spanish. The web release is deployed and production event/repayment discard checks passed without saving records. The user confirmed on the physical phone (29 October) that the Android guard works, using a standard native alert; individual forms and Keep/Discard paths were not itemised. Earlier expense/recurring device checks remain applicable. See FORM_UNSAVED_REVIEW.md for release status and limits.')
status_review['U01']=('Safeguards deployed; production startup checked','Vite now blocks missing or malformed Supabase build settings. Browser startup has an English/Spanish retry screen; static HTML retains a loading/reload fallback. The npm deployment command checks the expected home/profile shell and JavaScript entry. Local missing-config/Retry checks and production home/profile startup passed; all 23 web tests pass. No automatic rollback or notification-delivery verification is claimed. See STARTUP_REVIEW.md.')
for issue in issues:
 if issue['key']=='F02':
  issue['sources'].append({'author':'Live attendance verification','ref':'28 October 2026','url':'LIVE_ATTENDANCE_REVIEW.md','priority':status_review['F02'][0],'parts':[{'label':'Production web checks and native QA limit','text':status_review['F02'][1]}],'images':[{'src':'live-attendance-review/production-list-seats.png','alt':'Production Events list receives offered seats from a second event-detail view.'}]})
 if issue['key']=='F03':
  issue['sources'].append({'author':'Event and repayment guard verification','ref':'28 October 2026','url':'FORM_UNSAVED_REVIEW.md','priority':status_review['F03'][0],'parts':[{'label':'Additional form coverage and native QA limit','text':status_review['F03'][1]}],'images':[{'src':'form-unsaved-review/repayment-discard.png','alt':'A changed repayment requires explicit discard before Escape closes it.'}]})
 if issue['key']=='U01':
  issue['sources'].append({'author':'Startup safeguard verification','ref':'28 October 2026','url':'STARTUP_REVIEW.md','priority':status_review['U01'][0],'parts':[{'label':'Deployed safeguards and limits','text':status_review['U01'][1]}],'images':[{'src':'startup-review/missing-config.png','alt':'Local missing-config test shows a readable Axis retry screen.'},{'src':'startup-review/production-home.png','alt':'Production home loaded successfully after the safeguard release.'}]})
 if issue['key'] in release_status:
  issue['sources'].append({'author':'Production financial verification','ref':'28 October 2026','url':'RELEASE_MONEY_REVIEW.md','priority':release_status[issue['key']][0],'parts':[{'label':'Deployed web verification','text':release_status[issue['key']][1]}],'images':[{'src':'release-money-review/settled.png','alt':'Isolated production QA ledger after EUR2 partial and EUR3 remaining repayments cleared the EUR5 debt.'}] if issue['key']=='W4 / A2' else []})
 if issue['key'] in session_status:
  issue['sources'].append({'author':'Invitations and currency verification','ref':'30 October 2026','url':'REPORT_STATUS_REVIEW.md','priority':session_status[issue['key']][0],'parts':[{'label':'Current implementation and verification','text':session_status[issue['key']][1]}],'images':[]})
 if issue['key'] not in status_review:continue
 status,note=status_review[issue['key']]
 issue['status']=status
 issue['resolution']=note
 if issue['key']=='F03':
  issue['sources'].append({'author':'Android device verification','ref':'28 October 2026','url':'ANDROID_UNSAVED_REVIEW.md','priority':status,'parts':[{'label':'Native confirmation verified on the phone','text':note}],'images':[{'src':'android-unsaved-review/01-discard-dialog.png','alt':'Native Android discard confirmation for an edited expense.'}]})
  issue['sources'].append({'author':'Web browser verification','ref':'28 October 2026','url':'UNSAVED_WEB_REVIEW.md','priority':status,'parts':[{'label':'Verified locally with isolated mocked APIs','text':note}],'images':[{'src':'unsaved-web-review/discard-dialog.png','alt':'Axis custom discard confirmation with Keep editing selected by default.'}]})
 if issue['key']=='W3':
  issue['sources'].append({'author':'Calendar deployment verification','ref':'28 October 2026','url':'CALENDAR_FEED_REVIEW.md','priority':status,'parts':[{'label':'Deployed and checked against the live feed','text':note}],'images':[{'src':'calendar-feed-review/deployed.png','alt':'Supabase calendar-feed overview after deploying the UTC timestamp fix.'}]})
 if issue['key']=='W10':
  issue['sources'].append({'author':'Android device verification','ref':'28 October 2026','url':'ANDROID_SPLIT_MODE_REVIEW.md','priority':status,'parts':[{'label':'Verified on the connected phone','text':note}],'images':[{'src':'android-split-mode-review/01-equal-reopened.png','alt':'Saved Equal split reopened on the connected Android phone.'}]})
 if issue['key'] not in session_status:issue['sources'].append({'author':'Repository status review','ref':'28 October 2026','url':'REPORT_STATUS_REVIEW.md','priority':status,'parts':[{'label':'Current implementation and verification', 'text':note}],'images':[{'src':'date-validation-expense.png','alt':'Local web expense form retains 31/02/2026 and blocks saving with an inline date error.'}] if issue['key']=='W2' else []})
issues.sort(key=lambda x:(0,order[x['key']]) if x['key'] in order else (1 if x['priority']=='next' else 2, x['id']))
payload=json.dumps(issues,ensure_ascii=False).replace('</','<\\/')
template=(ROOT/'review_template.html').read_text(encoding='utf-8')
(ROOT/'REPORT_COMBINED.html').write_text(template.replace('__ISSUES__',payload),encoding='utf-8')
print(f'Built {len(issues)} issue cards; {sum(x["priority"]=="first" for x in issues)} guided cards; '+str((ROOT/'REPORT_COMBINED.html').stat().st_size)+' bytes')
