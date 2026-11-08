# -*- coding: utf-8 -*-
"""Trainer's guide (.docx) for the HCC course: how to run a session per role, a hands-on exercise per chapter on the demo build,
a half-day drill script, the answer keys (chapter quizzes and role checks) and an attendance sheet.
usage: python trainer_guide.py  -> build/Hotel Control Centre - Trainer's Guide.docx"""
import os, json
from docx import Document
from docx.shared import Pt, Cm, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT

HERE = os.path.dirname(os.path.abspath(__file__)); WEB = os.path.join(HERE, '..', 'hcc-web'); OUT = os.path.join(HERE, 'build'); os.makedirs(OUT, exist_ok=True)
steps = json.load(open(os.path.join(WEB, 'steps.json'), encoding='utf-8')); content = json.load(open(os.path.join(WEB, 'content.json'), encoding='utf-8'))
ns = {}; exec(open(os.path.join(HERE, '..', 'role-cards', 'cards_data.py'), encoding='utf-8').read(), ns); CARDS = ns['CARDS']
MAROON = RGBColor(0x7A, 0x1F, 0x2B)
d = Document()
for s in d.sections: s.left_margin = s.right_margin = Cm(2); s.top_margin = s.bottom_margin = Cm(2)
st = d.styles['Normal']; st.font.name = 'Calibri'; st.font.size = Pt(11)
for n, sz in (('Heading 1', 18), ('Heading 2', 14), ('Heading 3', 12)):
    h = d.styles[n]; h.font.name = 'Calibri'; h.font.size = Pt(sz); h.font.color.rgb = MAROON; h.font.bold = True
def H(t, l=1): return d.add_heading(t, l)
def P(t, bold=False, italic=False):
    p = d.add_paragraph(); r = p.add_run(t); r.bold = bold; r.italic = italic; return p
def B(t): return d.add_paragraph(t, style='List Bullet')
def N(t): return d.add_paragraph(t, style='List Number')
def table(rows, widths=None, head=True):
    t = d.add_table(rows=len(rows), cols=len(rows[0])); t.style = 'Table Grid'; t.alignment = WD_TABLE_ALIGNMENT.CENTER
    for i, row in enumerate(rows):
        for j, v in enumerate(row):
            c = t.cell(i, j); c.text = ''; r = c.paragraphs[0].add_run(str(v)); r.font.size = Pt(9.5); r.bold = head and i == 0
            if widths: c.width = Cm(widths[j])
    d.add_paragraph(); return t

t = d.add_paragraph(); r = t.add_run('Hotel Control Centre (HCC)'); r.font.size = Pt(26); r.bold = True; r.font.color.rgb = MAROON
t = d.add_paragraph(); r = t.add_run("Trainer's guide: how to teach HCC to every role"); r.font.size = Pt(16)
P('Companion of the training course (decks, videos in English and Hinglish, role cards). Fictional demo hotel (24 rooms), demo users, fictional guests; business date 24 Oct 2026 at the start, 26 Oct 2026 from chapter 9.', italic=True)

H('1. How the course is built')
B('25 chapters, one per module of HCC; each chapter follows the example guest Mr. Rahul Deshmukh (room 203, Pune) and the supporting records: Ms. Anita Rao (walk-in), Mr. Vikram Jadhav of Shree Agro Industries (company credit), the Patil wedding block, the Kulkarni engagement (hall) and the fictional staff of the demo hotel.')
B('Every screen is the real program with the control you need outlined in red and numbered. Slide notes carry the narration; the same text is the video voice.')
B('Role tracks: each person watches only the chapters of their role (the role card lists them). A chapter is 10 to 40 minutes of video in English or Hinglish.')
B('The demo hotel is not the hotel where you work: rooms, rates and names differ. On the real system you sign in with your own PIN.')
H('Before every session', 2)
for x in ('Open the Hotel Control Centre demo (training build, display mode Desktop) on a PC or a tablet for each trainee. Reset the demo before each exercise: Preview as ... > Reset.', 'Print the role card of each trainee (laminate it).', 'Check the projector shows the room board in full; text size Default.', 'Keep the TRAINING_NOTES in reach: they list the places where the demo behaves differently from the plan.'): B(x)

H('2. How to run a session for a role')
table([['Step', 'What you do', 'Time'], ['1', 'Explain the role in two sentences (role card, front side).', '5 min'], ['2', 'Play the chapters of the role track; stop at each recap, ask the three quiz questions, let trainees answer.', 'chapter length'],
       ['3', 'Hands-on: each trainee repeats the exercise of the chapter on the demo while you watch; fix wrong clicks at once.', '15 to 30 min per chapter'], ['4', 'Never-list: read the red box of the role card aloud.', '5 min'], ['5', 'Ten-question check (role card, back side); mark with the answer key; below 8 of 10 repeat the weak chapter.', '15 min'], ['6', 'Sign the attendance sheet.', '2 min']], [1.5, 12, 3.5])
H('Roles and chapters', 2)
roles = [(c[0], c[1], c[2], c[8]) for c in CARDS]
table([['Card', 'Role', 'Demo user', 'Chapters']] + [[str(a), b, c, e] for a, b, c, e in roles], [1.5, 4, 4, 7.5])

H('3. Hands-on exercise per chapter')
P('Each exercise is done on a freshly reset demo, signed in as the user of the chapter (Preview as ...). The chapters build on each other: a trainee who skips a chapter should reset and use the exercise data given here.')
for ch in steps['chapters']:
    H('Chapter %d: %s' % (ch['id'], ch['title']), 3)
    P('Work as %s. Goal: %s' % (ch['user'], ch['goal']))
    ex = [s for s in ch['steps'] if s['action'] in ('click', 'type', 'select', 'key')][:6]
    P('Exercise (first actions of the chapter):', bold=True)
    for s in ex: N('Step %s: %s' % (s['id'], s['say']))
    P('Done when: the trainee reaches the last screen of the chapter without help and can say why each action matters.', italic=True)

H('4. Half-day drill script (3 hours, up to 10 people)')
P('Purpose: practise the guest\'s day together in the demo, each person in their own role.')
table([['Time', 'Activity', 'Who'], ['0:00', 'Briefing: story of the day, roles, rules of the drill (no real money, no real names).', 'Trainer'], ['0:15', 'Reset the demo. Reception books Mr. Rahul Deshmukh (chapter 1) and checks him in (chapter 2) with an advance of Rs 2,000 by UPI.', 'Reception'],
       ['0:45', 'Reception logs towels, a wake-up call and a complaint; housekeeping handles the towels (chapter 4, 9).', 'Reception, Housekeeping'], ['1:15', 'Restaurant check for two guests, room-service dinner to the folio (chapter 6). Cashier adds laundry and takes the payment by card (chapter 5, 8).', 'Cashier, Restaurant'],
       ['1:45', 'Manager approves a discount, a company credit and an expense; maintenance closes the air-conditioner work order (chapter 5, 8, 10, 17).', 'Manager, Maintenance'], ['2:15', 'Check-out, invoice and credit note request; Owner approves it (chapter 8).', 'Reception, Owner'],
       ['2:30', 'Accountant reads the vouchers, the ageing and the cash book; Owner reads the digest (chapter 20, 21).', 'Accountant, Owner'], ['2:45', 'Debrief: what was slow, what was refused and why; each person names one never-rule.', 'All']], [2, 12, 3])
P('Observation sheet: for each trainee note wrong clicks, questions asked, and whether they used the keys and the search. Drill report: date, people, what went well, what to repeat, owner of each repeat.')

H('5. Answer keys')
H('Chapter quizzes (three questions each)', 2)
for k in sorted(content['recap'], key=int):
    r = content['recap'][k]; H('Chapter %s' % k, 3)
    for i, q in enumerate(r['quiz'], 1): P('%d. %s' % (i, q['q'])); P('   Answer: %s' % q['a'], bold=True)
H('Role checks (ten questions each)', 2)
for c in CARDS:
    H('RC-%d %s' % (c[0], c[1]), 3)
    for i, (q, a) in enumerate(c[9], 1): P('%d. %s  Answer: %s' % (i, q, a))

H('6. Attendance sheet')
table([['Date', 'Trainer', 'Name', 'Role', 'Chapters taken', 'Check (of 10)', 'Signature']] + [['', '', '', '', '', '', ''] for _ in range(14)], [2, 2.2, 3.2, 2.5, 3, 2, 2.5])
H('7. Known differences between the demo and a real hotel', 2)
for x in ('The demo has a staff picker instead of PINs; the production server signs in with a PIN or password.', 'E-mail, WhatsApp, OCR and the key encoder are simulated; the demo says so on screen.', 'The invoice series in the demo is DEMO/2026-27; the hotel\'s own series comes from its masters.',
          'Some actions are refused in the demo on purpose (a discount above 25 per cent of the charges, a cancel with money held): this is the program\'s rule, not a fault.', 'The details are in TRAINING_NOTES.md of the course folder.'): B(x)
path = os.path.join(OUT, "Hotel Control Centre - Trainer's Guide.docx"); d.save(path); print(path)
