# -*- coding: utf-8 -*-
"""Ten HCC role cards (A4: page 1 the card, page 2 the ten-question check with the answer key) -> build/RC-<n> <role>.pdf.
Style after the Manual System role cards, HCC maroon #7A1F2B. Facts come from the course (steps.json, chapters) of the demo hotel.
usage: python role_cards.py"""
import os
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, 'build'); os.makedirs(OUT, exist_ok=True)
pdfmetrics.registerFont(TTFont('Arial', 'C:/Windows/Fonts/arial.ttf')); pdfmetrics.registerFont(TTFont('Arial-Bold', 'C:/Windows/Fonts/arialbd.ttf'))
F, FB = 'Arial', 'Arial-Bold'
MAROON, DARK, INK, GREY, RED, PALE, LIGHT = (colors.HexColor(x) for x in ('#7A1F2B', '#5A1620', '#2B1D16', '#6B5547', '#B00020', '#F6EAEC', '#FAF6F0'))
W, H = A4; M = 10 * mm

def wrap(c, text, width, font, size):
    words = text.split(); line = ''; out = []
    for wd in words:
        t = (line + ' ' + wd).strip()
        if c.stringWidth(t, font, size) > width and line: out.append(line); line = wd
        else: line = t
    if line: out.append(line)
    return out

def para(c, x, y, w, text, font=F, size=8.6, lead=None, color=INK):
    lead = lead or size * 1.28; c.setFont(font, size); c.setFillColor(color)
    for ln in wrap(c, text, w, font, size): c.drawString(x, y, ln); y -= lead
    return y

def band(c, x, y, w, title, color=INK):
    c.setFillColor(color); c.rect(x, y - 6 * mm, w, 6 * mm, stroke=0, fill=1); c.setFillColor(colors.white); c.setFont(FB, 9); c.drawString(x + 2 * mm, y - 4.2 * mm, title); return y - 6 * mm

def header(c, n, role, tagline, page2=False):
    c.setFillColor(MAROON); c.rect(0, H - 26 * mm, W, 26 * mm, stroke=0, fill=1)
    c.setFillColor(colors.white); c.setFont(FB, 19); c.drawString(M, H - 12 * mm, 'ROLE CARD RC-%d  |  %s' % (n, role.upper()))
    c.setFont(F, 9.5); c.drawString(M, H - 18 * mm, tagline); c.drawString(M, H - 23 * mm, 'Hotel Control Centre (HCC) training  |  fictional demo hotel, demo users  |  ' + ('answer key at the bottom' if page2 else 'laminate; keep at your station'))

def card(n, role, user, tagline, screens, day, facts, never, chapters, quiz):
    path = os.path.join(OUT, 'RC-%d %s.pdf' % (n, role.replace('/', '-')))
    c = canvas.Canvas(path, pagesize=A4); c.setTitle('HCC role card %d: %s' % (n, role)); c.setAuthor('Hotel Control Centre training')
    header(c, n, role, tagline)
    y = H - 32 * mm; lw = 92 * mm; rx = M + lw + 5 * mm; rw = W - M - rx
    # left: screens
    yy = band(c, M, y, lw, 'SCREENS YOU WORK IN  (Modules Home > tile > page)')
    for i, (menu, what) in enumerate(screens):
        lines = wrap(c, what, 52 * mm, F, 9); lm = wrap(c, menu, 33 * mm, FB, 9)
        rh = max(len(lines), len(lm)) * 4.2 * mm + 3 * mm
        c.setFillColor(LIGHT if i % 2 else colors.white); c.rect(M, yy - rh, lw, rh, stroke=0, fill=1)
        c.setStrokeColor(colors.HexColor('#E3D9CF')); c.setLineWidth(0.4); c.line(M, yy - rh, M + lw, yy - rh)
        ty = yy - 4 * mm; c.setFillColor(MAROON); c.setFont(FB, 9)
        for ln in lm: c.drawString(M + 1.5 * mm, ty, ln); ty -= 4.2 * mm
        ty = yy - 4 * mm; c.setFillColor(INK); c.setFont(F, 9)
        for ln in lines: c.drawString(M + 38 * mm, ty, ln); ty -= 4.2 * mm
        yy -= rh
    yy -= 3 * mm
    yy = band(c, M, yy, lw, 'YOUR NORMAL DAY', MAROON)
    yy -= 5.5 * mm
    for i, s in enumerate(day, 1):
        c.setFillColor(MAROON); c.setFont(FB, 10); c.drawString(M + 1 * mm, yy, '%d' % i); yy = para(c, M + 6 * mm, yy, lw - 8 * mm, s, size=10, lead=4.8 * mm); yy -= 0.8 * mm
    # right: key facts, never, chapters
    ry = band(c, rx, y, rw, 'KEY FACTS')
    ry -= 5.5 * mm
    for s in facts:
        c.setFillColor(MAROON); c.setFont(FB, 8.6); c.drawString(rx + 1 * mm, ry, '-'); ry = para(c, rx + 4.5 * mm, ry, rw - 6 * mm, s, size=10, lead=4.8 * mm); ry -= 0.8 * mm
    ry -= 2 * mm
    ry = band(c, rx, ry, rw, 'NEVER', RED); ry -= 5.5 * mm
    for s in never:
        c.setFillColor(RED); c.setFont(FB, 8.6); c.drawString(rx + 1 * mm, ry, 'x'); ry = para(c, rx + 4.5 * mm, ry, rw - 6 * mm, s, size=10, lead=4.8 * mm); ry -= 0.8 * mm
    ry -= 2 * mm
    ry = band(c, rx, ry, rw, 'TRAINING VIDEOS (chapters, English and Hinglish)', DARK); ry -= 5.5 * mm
    ry = para(c, rx + 1 * mm, ry, rw - 2 * mm, chapters, size=10, lead=4.8 * mm)
    c.setFillColor(GREY); c.setFont(F, 7.5); c.drawString(M, 8 * mm, 'Signed in as %s in the training. On the real system you sign in with your own PIN; everything you do is recorded under your name.' % user)
    c.showPage()
    # page 2: check
    header(c, n, role, 'Ten-question check', page2=True)
    y = H - 34 * mm
    for i, (q, a) in enumerate(quiz, 1):
        c.setFillColor(MAROON); c.setFont(FB, 10); c.drawString(M, y, '%d.' % i)
        y = para(c, M + 8 * mm, y, W - 2 * M - 10 * mm, q, size=10, lead=4.6 * mm)
        c.setStrokeColor(colors.HexColor('#C9BDB1')); c.setLineWidth(0.4); c.line(M + 8 * mm, y - 1 * mm, W - M, y - 1 * mm); y -= 7 * mm
    y -= 3 * mm
    y = band(c, M, y, W - 2 * M, 'ANSWER KEY', MAROON); y -= 3 * mm
    for i, (q, a) in enumerate(quiz, 1):
        c.setFillColor(MAROON); c.setFont(FB, 8.8); c.drawString(M + 1 * mm, y, '%d.' % i); y = para(c, M + 8 * mm, y, W - 2 * M - 10 * mm, a, size=8.8, lead=4 * mm); y -= 0.8 * mm
    c.save(); return path

exec(open(os.path.join(HERE, 'cards_data.py'), encoding='utf-8').read())
for args in CARDS: print(card(*args))
