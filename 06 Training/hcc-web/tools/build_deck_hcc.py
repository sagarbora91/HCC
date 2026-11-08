"""Step 4+5: build the 16:9 training deck and the English narration script from steps.json + shots + content.json.
usage: python build_deck.py --config training.config.json
Slide kinds (config deck.include, default all): title, chapter, steps, recap, final.
 - step slide: screenshot 70 % left, text panel 30 % right (>= 18 pt, trimming ladder so nothing overflows)
 - recap slide: what you learned + quiz (answers only in notes); final slides: quick-reference card, menu map, course outline
Speaker notes = narration. Writes <name>.pptx, the narration markdown (languages.<base>.notes) and deck/slides.json (manifest used by tts/encode/check)."""
import json, os, re, sys
HERE = r'C:/Users/Sagar/.claude/skills/training-media/scripts'; sys.path.insert(0, HERE)  # copy of the skill's build_deck.py (HCC: 3-column outline for 25 chapters)
import tmlib, measure
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.oxml import parse_xml
from PIL import Image

args = tmlib.parser(__doc__).parse_args(); cfg = tmlib.load(args.config)
D = cfg['deck']; INC = set(D.get('include', ['title', 'chapter', 'steps', 'recap', 'final']))
rgb = lambda h: RGBColor.from_string(h.lstrip('#').upper())
NAVY, INK, GREY, BLUE, RED = rgb(D.get('navy', '1F2A5C')), rgb('202020'), rgb('555555'), rgb('003DA5'), rgb(D.get('accent', 'B00020'))
WHITE, SOFT = RGBColor(255, 255, 255), RGBColor(0xDD, 0xDD, 0xEE)
FOOT = D.get('footer', tmlib.name(cfg)); FONT = D.get('font', 'Calibri'); MINPT = D.get('min_pt', 18)
SW, SH = 13.333, 7.5
data = tmlib.steps(cfg); CH = data['chapters']
content = json.load(open(tmlib.P(cfg, D['content']), encoding='utf8')) if D.get('content') else {}
RECAP = content.get('recap', {}); FKEYS = content.get('fkeys', []); MENUMAP = content.get('menumap', []); EXTRA = content.get('reference_lines', {})
shots_dir = tmlib.out(cfg, 'shots')
IMG_X, IMG_Y, IMG_W = 0.25, 0.85, 9.33
PAN_X, PAN_Y, PAN_W, PAN_H = 9.72, 0.25, 3.4, 6.75
prs = Presentation(); prs.slide_width = Inches(SW); prs.slide_height = Inches(SH); BLANK = prs.slide_layouts[6]
NOTES = []; MAN = []; TRIMMED = []

def tb(slide, x, y, w, h, paras, anchor=MSO_ANCHOR.TOP, align=None, margin=0.05):
    s = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h)); tf = s.text_frame
    tf.word_wrap = True; tf.vertical_anchor = anchor
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = Inches(margin)
    for k, p in enumerate(paras):
        para = tf.paragraphs[0] if k == 0 else tf.add_paragraph()
        runs = p['runs'] if 'runs' in p else [dict(text=p['text'], bold=p.get('bold', False), color=p.get('color', INK))]
        for r in runs:
            run = para.add_run(); run.text = r['text']; f = run.font
            f.size = Pt(p.get('pt', MINPT)); f.bold = r.get('bold', p.get('bold', False)); f.italic = p.get('italic', False); f.name = FONT
            f.color.rgb = r.get('color', p.get('color', INK))
        para.space_after = Pt(p.get('after', 0)); para.line_spacing = 1.0
        if align: para.alignment = align
    return s

def footer(slide, dark=False):
    col = SOFT if dark else GREY
    tb(slide, 0.25, 7.05, 9.0, 0.38, [dict(text=FOOT, pt=MINPT, color=col)], anchor=MSO_ANCHOR.MIDDLE)
    s = slide.shapes.add_textbox(Inches(11.9), Inches(7.05), Inches(1.2), Inches(0.38)); tf = s.text_frame
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = Inches(0.05); tf.vertical_anchor = MSO_ANCHOR.MIDDLE
    p = tf.paragraphs[0]; p.alignment = PP_ALIGN.RIGHT
    p._p.append(parse_xml('<a:fld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" id="{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}" type="slidenum"><a:rPr lang="en-US" sz="%d"><a:solidFill><a:srgbClr val="%s"/></a:solidFill><a:latin typeface="%s"/></a:rPr><a:t>&#8249;#&#8250;</a:t></a:fld>' % (MINPT * 100, '%02X%02X%02X' % tuple(col), FONT)))

def new_slide(notes, label, kind, ch=None, step=None, dark=False):
    s = prs.slides.add_slide(BLANK); f = s.background.fill; f.solid(); f.fore_color.rgb = NAVY if dark else WHITE
    s.notes_slide.notes_text_frame.text = notes
    NOTES.append((label, notes)); MAN.append(dict(n=len(NOTES), kind=kind, chapter=ch, step=step, label=label))
    return s

def fit(paras, w, h):
    return measure.height_in([(p['text'] if 'text' in p else ''.join(r['text'] for r in p['runs']), p.get('pt', MINPT), p.get('bold', False), p.get('after', 0)) for p in paras], w) <= h

def short(t, n):
    t = ' '.join(t.split()); return t if len(t) <= n else t[:n - 1].rstrip() + '…'

def narration_step(s):
    t = "Step %s. %s" % (s['id'], s['say'].strip())
    if s.get('why'): t += " " + s['why'].strip()
    if s.get('value'): t += " Type: %s." % s['value']
    if s.get('expect'): t += " Expected: %s" % s['expect'].strip()
    mt = s.get('msgText') or s.get('msg')
    if mt: t += " The program shows the message: %s" % mt
    if s.get('msgwhy'): t += " " + s['msgwhy'].strip()
    return t

def chapter_menu(c):
    ms = [s['menu'] for s in c['steps'] if s.get('menu')]; ms = list(dict.fromkeys(ms))
    if c.get('menu'): ms.insert(0, c['menu'])
    return '; '.join(ms[:3]) if ms else D.get('default_menu_text', 'the home screen')

nsteps = sum(len(c['steps']) for c in CH); nch = len(CH)
if 'title' in INC:
    t0 = D.get('welcome_narration') or ("Welcome to the %s training. In %d chapters you follow one example from start to finish. Every slide shows the real program with the control you need outlined in red and numbered." % (D.get('product', tmlib.name(cfg)), nch))
    s = new_slide(t0, 'Title', 'title', dark=True)
    tb(s, 0.8, 1.5, 11.7, 1.6, [dict(text=D.get('title', tmlib.name(cfg)), pt=48, bold=True, color=WHITE)])
    tb(s, 0.8, 2.9, 11.7, 1.2, [dict(text=D.get('subtitle', ''), pt=36, color=WHITE)])
    tb(s, 0.8, 4.4, 11.7, 1.6, [dict(text="%d chapters, %d steps" % (nch, nsteps), pt=24, color=SOFT, after=8), dict(text=D.get('disclaimer', ''), pt=20, color=SOFT)])
    footer(s, dark=True)

for c in CH:
    cid = c['id']; menu = chapter_menu(c); n = len(c['steps'])
    if 'chapter' in INC:
        narr = "Chapter %d: %s. The goal of this chapter: %s You work as user %s. Where to go: %s. This chapter has %d steps." % (cid, c['title'], c['goal'], c.get('user', ''), menu, n)
        s = new_slide(narr, 'Chapter %d title' % cid, 'chapter', ch=cid, dark=True)
        tb(s, 0.8, 0.9, 11.7, 0.6, [dict(text="Chapter %d of %d" % (cid, CH[-1]['id']), pt=24, color=SOFT)])
        tb(s, 0.8, 1.5, 11.7, 1.7, [dict(text=c['title'], pt=40, bold=True, color=WHITE)])
        tb(s, 0.8, 3.3, 11.7, 3.5, [dict(runs=[dict(text=k + ": ", bold=True, color=WHITE), dict(text=v, color=WHITE)], pt=24, after=10)
                                    for k, v in (("Goal", c['goal']), ("User", c.get('user', '')), ("Menu path", menu), ("Steps", str(n)))])
        footer(s, dark=True)
    if 'steps' in INC:
        for idx, st in enumerate(c['steps'], 1):
            s = new_slide(narration_step(st), 'Step %s' % st['id'], 'step', ch=cid, step=st['id'])
            img = os.path.join(shots_dir, st['shot'])
            if not os.path.exists(img): sys.exit('missing screenshot %s (run capture.js)' % img)
            iw, ih = Image.open(img).size; IMG_H = IMG_W * ih / iw
            tb(s, IMG_X, 0.2, IMG_W, 0.6, [dict(text="Chapter %d – %s" % (cid, c['title']), pt=20, bold=True, color=NAVY)], anchor=MSO_ANCHOR.MIDDLE)
            pic = s.shapes.add_picture(img, Inches(IMG_X), Inches(IMG_Y), width=Inches(IMG_W)); pic.line.color.rgb = RGBColor(0x80, 0x80, 0x80); pic.line.width = Pt(1)
            where = [x for x in (st.get('menu'), ("key " + st['key']) if st.get('key') else '') if x]
            loc = ("User %s" % c['user'] if c.get('user') else '') + (" · " + " · ".join(where) if where else '')
            tb(s, IMG_X, IMG_Y + IMG_H + 0.08, IMG_W, 0.85, [dict(text=short(loc, 150), pt=MINPT, color=GREY)])
            def panel(level):
                ps = [dict(text="Step %s  (%d of %d)" % (st['id'], idx, n), pt=20, bold=True, color=NAVY, after=6), dict(text=st['say'].strip(), pt=MINPT, after=6)]
                if st.get('why') and level < 2:
                    w = st['why'].strip()
                    if level == 1: w = re.split(r'(?<=[.!?]) ', w)[0]
                    ps.append(dict(runs=[dict(text="Why: ", bold=True, color=NAVY), dict(text=w)], pt=MINPT, after=6))
                if st.get('value'): ps.append(dict(runs=[dict(text="Type: ", bold=True, color=RED), dict(text=st['value'], bold=True, color=RED)], pt=MINPT, after=6))
                if st.get('expect'): ps.append(dict(runs=[dict(text="Result: ", bold=True, color=NAVY), dict(text=short(st['expect'].strip(), 60) if level >= 3 else st['expect'].strip())], pt=MINPT, after=6))
                mt = st.get('msgText') or st.get('msg')
                if mt: ps.append(dict(runs=[dict(text="Message: ", bold=True, color=BLUE), dict(text='"%s"' % short(mt, 80), color=BLUE)], pt=MINPT, after=0))
                return ps
            lvl = 0
            while lvl < 4 and not fit(panel(lvl), PAN_W, PAN_H): lvl += 1
            if lvl: TRIMMED.append((st['id'], lvl))
            tb(s, PAN_X, PAN_Y, PAN_W, PAN_H, panel(lvl)); footer(s)
    if 'recap' in INC:
        if str(cid) not in RECAP: sys.exit('content.json recap is missing chapter %s (see templates/content.template.json)' % cid)
        learned, quiz = RECAP[str(cid)]['learned'], RECAP[str(cid)]['quiz']
        narr = "Chapter %d recap, what you learned. " % cid + " ".join(learned) + " Quiz. " + " ".join("Question %d: %s" % (i + 1, q['q']) for i, q in enumerate(quiz)) + " Answers. " + " ".join("Answer %d: %s" % (i + 1, q['a']) for i, q in enumerate(quiz))
        s = new_slide(narr, 'Chapter %d recap' % cid, 'recap', ch=cid)
        tb(s, 0.5, 0.25, 12.3, 0.7, [dict(text="Chapter %d recap" % cid, pt=28, bold=True, color=NAVY)])
        ps = [dict(text=c['title'], pt=MINPT, color=GREY, after=6), dict(text="What you learned", pt=22, bold=True, color=NAVY, after=4)]
        ps += [dict(text="• " + l, pt=MINPT, after=4) for l in learned]
        ps.append(dict(text="Quiz (answers are in the speaker notes)", pt=22, bold=True, color=NAVY, after=4))
        ps += [dict(text="%d. %s" % (i + 1, q['q']), pt=MINPT, after=4) for i, q in enumerate(quiz)]
        assert fit(ps, 12.3, 5.9), 'recap overflow chapter %d: shorten content.json' % cid
        tb(s, 0.5, 1.0, 12.3, 5.95, ps); footer(s)

if 'final' in INC:
    narr = "Quick reference card. The function keys: " + "; ".join("%s is %s" % (k, v) for k, v in FKEYS) + ". " + EXTRA.get('keyboard_narration', '') + " " + EXTRA.get('passwords_narration', '')
    s = new_slide(narr.strip(), 'Quick reference card', 'final')
    tb(s, 0.5, 0.25, 12.3, 0.7, [dict(text="Quick-reference card", pt=30, bold=True, color=NAVY)])
    tb(s, 0.5, 1.0, 6.0, 5.9, [dict(text="Function keys", pt=22, bold=True, color=NAVY, after=4)] + [dict(runs=[dict(text=k + "  ", bold=True, color=RED), dict(text=v)], pt=MINPT, after=2) for k, v in FKEYS])
    right = [dict(text="Keyboard", pt=22, bold=True, color=NAVY, after=4)] + [dict(text=t, pt=MINPT, after=2) for t in EXTRA.get('keyboard', [])]
    right += [dict(text="Demo passwords (training only)", pt=22, bold=True, color=NAVY, after=4)] + [dict(text=t, pt=MINPT, after=2) for t in EXTRA.get('passwords', [])]
    assert fit(right, 6.0, 5.9), 'quick-reference right column overflows'
    tb(s, 6.9, 1.0, 6.0, 5.9, right); footer(s)
    narr = "Where to find every menu. " + " ".join("%s: %s." % (k, v) for k, v in MENUMAP)
    s = new_slide(narr, 'Where to find every menu', 'final')
    tb(s, 0.5, 0.2, 12.3, 0.65, [dict(text="Where to find every menu", pt=30, bold=True, color=NAVY)])
    cols = [MENUMAP[i::3] for i in range(3)]
    for x, w, items in ((0.35, 4.1, cols[0]), (4.6, 4.1, cols[1]), (8.85, 4.2, cols[2])):
        ps = []
        for k, v in items: ps += [dict(text=k, pt=MINPT, bold=True, color=NAVY), dict(text=v, pt=MINPT, after=5)]
        assert fit(ps, w, 6.1), 'menu map overflow: shorten menumap entries'
        tb(s, x, 0.95, w, 6.1, ps)
    footer(s)
    outline = ["%d. %s – %d steps%s" % (c['id'], c['title'], len(c['steps']), " (%s)" % c['user'] if c.get('user') else '') for c in CH]
    s = new_slide("Course outline. " + " ".join(outline), 'Course outline', 'final')
    tb(s, 0.5, 0.2, 12.3, 0.65, [dict(text="Course outline", pt=30, bold=True, color=NAVY)])
    outline = ["%d. %s (%d)" % (c['id'], c['title'], len(c['steps'])) for c in CH]
    cols = [outline[i::3] for i in range(3)]
    for x, w, items in ((0.35, 4.1, cols[0]), (4.6, 4.1, cols[1]), (8.85, 4.2, cols[2])):
        ps = [dict(text=o, pt=MINPT, after=6) for o in items]
        assert fit(ps, w, 6.1), 'outline overflow'
        tb(s, x, 0.95, w, 6.1, ps)
    footer(s)

o = tmlib.out(cfg, tmlib.name(cfg) + '.pptx'); tmlib.mk(os.path.dirname(o)); prs.save(o)
base = cfg.get('base_language', 'en'); notes_md = tmlib.lang_notes_path(cfg, base); tmlib.mk(os.path.dirname(notes_md))
with open(notes_md, 'w', encoding='utf8') as f:
    f.write("# %s – narration script\n\nOne block per slide; the same text is in the speaker notes of the deck.\n\n" % tmlib.name(cfg))
    for i, (label, text) in enumerate(NOTES, 1): f.write("## Slide %d – %s\n\n%s\n\n" % (i, label, text))
tmlib.mk(os.path.dirname(tmlib.manifest_path(cfg)))
json.dump(dict(slides=MAN, trimmed=TRIMMED), open(tmlib.manifest_path(cfg), 'w'), indent=1)
print('slides', len(NOTES), 'trimmed', len(TRIMMED), 'pptx MB', round(os.path.getsize(o) / 1e6, 1), '->', o)
