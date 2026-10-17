#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Генератор 50 обоев 1920x1080 с мотивирующими цитатами об учении.

Требуется: Pillow  (python -m pip install Pillow)
Запуск:    python generate_wallpapers.py
Результат: 01_*.png ... 50_*.png + index.html в этой же папке.
"""

import colorsys
import html
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

# ----------------------------------------------------------------------------
# Базовые настройки
# ----------------------------------------------------------------------------
S = 2                                # суперсэмплинг: рисуем 3840x2160 -> 1920x1080
DW, DH = 1920, 1080                  # целевой размер
W, H = DW * S, DH * S                # рабочая канва
OUT_DIR = os.path.dirname(os.path.abspath(__file__))

FONT_FILES = {
    "quote":  r"C:\Windows\Fonts\georgia.ttf",
    "mark":   r"C:\Windows\Fonts\georgia.ttf",
    "author": r"C:\Windows\Fonts\segoeuib.ttf",
    "label":  r"C:\Windows\Fonts\segoeui.ttf",
}
_FONT_CACHE = {}


def s(v):
    """Дизайнерские пиксели -> пиксели рабочей канвы."""
    return int(round(v * S))


def F(kind, size):
    key = (kind, size)
    if key not in _FONT_CACHE:
        _FONT_CACHE[key] = ImageFont.truetype(FONT_FILES[kind], size)
    return _FONT_CACHE[key]


# ----------------------------------------------------------------------------
# Цветовые палитры (50)
# ----------------------------------------------------------------------------
def PAL(bg, accent, glow):
    return {"bg": bg, "accent": accent, "glow": glow}


def _hsv(h, sat, val):
    r, g, b = colorsys.hsv_to_rgb((h % 360) / 360.0, sat, val)
    return (int(round(r * 255)), int(round(g * 255)), int(round(b * 255)))


def _make_palettes(n):
    """Тёмные палитры: равномерный разброс оттенков + контрастный металлик."""
    hues = [
        222, 350, 165, 42, 275, 200, 12, 305, 150, 240,
        88, 330, 190, 25, 260, 135, 210, 55, 295, 175,
        320, 100, 235, 20, 285, 160, 45, 250, 110, 340,
        195, 70, 270, 145, 15, 225, 315, 90, 205, 35,
        255, 125, 180, 300, 60, 215, 335, 155, 245, 80,
    ]
    pals = []
    for i in range(n):
        h = hues[i % len(hues)]
        bg = [
            _hsv(h - 8, 0.78, 0.15),     # верх-лево
            _hsv(h + 10, 0.68, 0.24),    # верх-право
            _hsv(h + 3, 0.88, 0.08),     # низ-лево
            _hsv(h - 14, 0.74, 0.19),    # низ-право
        ]
        warm = 0 <= h < 90
        if warm or i % 5 == 2:
            accent = _hsv(196, 0.20, 0.96)      # холодный жемчуг
        else:
            accent = _hsv(44 + (i * 7) % 11, 0.45, 0.95)   # тёплое золото
        glow = _hsv(h + 4, 0.85, 0.72)
        pals.append(PAL(bg, accent, glow))
    return pals


PALETTES = _make_palettes(50)

# ----------------------------------------------------------------------------
# Мотивирующие цитаты об учении (автор, текст, палитра, шаблон композиции)
# ----------------------------------------------------------------------------
QUOTES = [
    ("Нельсон Мандела",       "Образование — самое мощное оружие, с помощью которого можно изменить мир.", 0),
    ("Аристотель",            "Корни образования горьки, но плоды его сладки.", 1),
    ("Альберт Эйнштейн",      "Образование — это то, что остаётся после того, как забыто всё, чему учили в школе.", 2),
    ("Махатма Ганди",         "Живи так, будто умрёшь завтра. Учись так, будто будешь жить вечно.", 3),
    ("Бенджамин Франклин",    "Инвестиции в знания платят наибольшие проценты.", 4),
    ("Конфуций",              "Учение без размышления бесполезно, но и размышление без учения опасно.", 5),
    ("Конфуций",              "Скажи мне — и я забуду. Покажи мне — и я запомню. Дай мне сделать — и я пойму.", 6),
    ("Герберт Спенсер",       "Великая цель образования — не знания, а действия.", 7),
    ("Карл Роджерс",          "Единственный по-настоящему образованный человек — тот, кто научился учиться и меняться.", 8),
    ("Джим Рон",              "Формальное образование поможет вам выжить. Самообразование приведёт вас к успеху.", 9),
    ("Генри Форд",            "Кто перестаёт учиться — стар, будь ему двадцать или восемьдесят.", 10),
    ("К. С. Станиславский",   "Каждый день, в который вы не пополнили своего образования хотя бы маленьким, но новым для вас знанием, считайте бесплодно и невозвратно для себя погибшим.", 11),
    ("Фрэнсис Бэкон",         "Знание — сила.", 12),
    ("А. С. Пушкин",          "Чтение — вот лучшее учение.", 13),
    ("Сенека",                "Век живи — век учись.", 14),
    ("Томас Фуллер",          "Кто ни о чём не спрашивает, тот ничему не научится.", 15),
    ("Роберт Кольер",         "Успех — это сумма маленьких усилий, повторяемых изо дня в день.", 16),
    ("Конфуций",              "Учитесь так, словно вы постоянно ощущаете нехватку своих знаний, и так, словно вы постоянно боитесь растерять их.", 17),
    ("Авраам Линкольн",       "Я буду готовиться, и однажды мой шанс придёт.", 18),

    ("Сократ",                "Я знаю только то, что ничего не знаю.", 19),
    ("Иммануил Кант",         "Имей мужество пользоваться собственным умом.", 20),
    ("Джон Дьюи",             "Образование — не подготовка к жизни, а сама жизнь.", 21),
    ("Дмитрий Менделеев",     "Нет без явного усиленного трудолюбия ни талантов, ни гениев.", 22),
    ("Лев Толстой",           "Знание — орудие, а не цель.", 23),
    ("Александр Суворов",     "Тяжело в учении — легко в бою.", 24),
    ("Михаил Ломоносов",      "Неусыпный труд препятствия преодолевает.", 25),
    ("К. Д. Ушинский",        "Ученье есть труд и должно остаться трудом, но трудом, полным мысли.", 26),
    ("В. А. Сухомлинский",    "Учитель готовится к хорошему уроку всю жизнь.", 27),
    ("Мария Монтессори",      "Помоги мне сделать это самому.", 28),
    ("Жан Пиаже",             "Когда мы учим ребёнка чему-то, мы мешаем ему открыть это самому.", 29),
    ("Альберт Эйнштейн",      "Кто никогда не делал ошибок, тот никогда не пробовал ничего нового.", 30),
    ("Плутарх",               "Ум — это не сосуд, который нужно наполнить, а огонь, который нужно разжечь.", 31),
    ("Уинстон Черчилль",      "Я всегда готов учиться, но мне не всегда нравится, когда меня учат.", 32),
    ("Уинстон Черчилль",      "Успех — это способность шагать от одной неудачи к другой, не теряя энтузиазма.", 33),
    ("Марк Твен",             "Через двадцать лет вы будете больше сожалеть о том, чего не сделали, чем о том, что сделали.", 34),
    ("Рене Декарт",           "Чтение хороших книг — это разговор с самыми лучшими людьми прошлых веков.", 35),
    ("Иоганн Вольфганг Гёте", "Знать недостаточно, надо применять. Хотеть недостаточно, надо делать.", 36),
    ("Лао-цзы",               "Путь в тысячу ли начинается с первого шага.", 37),
    ("Лао-цзы",               "Знающий других — умён, знающий себя — просвещён.", 38),
    ("Малкольм Икс",          "Образование — это паспорт в будущее: завтрашний день принадлежит тем, кто готовится к нему сегодня.", 39),
    ("Джим Рон",              "Дисциплина — это мост между целью и её достижением.", 40),
    ("Ральф Уолдо Эмерсон",   "Всякий человек, которого я встречаю, в чём-то меня превосходит, и этому я могу у него учиться.", 41),
    ("Альберт Эйнштейн",      "Воображение важнее знания.", 42),
    ("Аристотель",            "Все люди по природе стремятся к знанию.", 43),
    ("Фрэнсис Бэкон",         "Чтение делает человека знающим, беседа — находчивым, а привычка записывать — точным.", 44),
    ("Сенека",                "Уча других, мы учимся сами.", 45),
    ("Русская пословица",     "Ученье — свет, а неученье — тьма.", 46),
    ("Русская пословица",     "Повторение — мать учения.", 47),
    ("Русская пословица",     "Не стыдно не знать, стыдно не учиться.", 48),
    ("Китайская пословица",   "Обучение — это сокровище, которое всюду следует за своим владельцем.", 49),
]

TOTAL = len(QUOTES)

SLUGS = [
    "mandela-obrazovanie-oruzhie", "aristotel-korni-obrazovaniya", "einstein-obrazovanie",
    "gandi-uchis-vechno", "franklin-investitsii-v-znaniya", "konfutsiy-razmyshlenie",
    "konfutsiy-skazhi-mne", "spenser-deystviya", "rodzhers-uchitsya-menyatsya",
    "dzhim-ron-samoobrazovanie", "ford-kto-perestaet-uchitsya", "stanislavskiy-kazhdyy-den",
    "bekon-znanie-sila", "pushkin-chtenie", "seneka-vek-zhivi",
    "fuller-kto-ne-sprashivaet", "koller-uspeh", "konfutsiy-nehvatka-znaniy",
    "linkoln-budu-gotovitsya", "sokrat-ya-znayu-chto-nichego-ne-znayu", "kant-muzhestvo-uma",
    "dyui-obrazovanie-zhizn", "mendeleev-trudolyubie", "tolstoy-znanie-orudie",
    "suvorov-tyazhelo-v-uchenii", "lomonosov-neusypnyy-trud", "ushinsky-uchenie-trud",
    "suhomlinsky-urok", "montessori-pomogi-mne", "piazhe-otkryt-samomu",
    "einstein-oshibki", "plutarh-ogon", "churchill-gotov-uchitsya",
    "churchill-uspeh", "twain-cherez-dvadtsat-let", "dekart-chtenie",
    "gete-znat-nedostatochno", "laotszy-put-v-tysyachu-li", "laotszy-znayushchiy-sebya",
    "malcolm-x-pasport-v-budushchee", "dzhim-ron-distsiplina", "emerson-prevoshodit",
    "einstein-voobrazhenie", "aristotel-stremlenie-k-znaniyu", "bekon-chtenie",
    "seneka-ucha-drugih-uchimsya-sami", "poslovitsa-uchenie-svet", "poslovitsa-povtorenie-mat-ucheniya",
    "poslovitsa-ne-stydno-ne-znat", "poslovitsa-obuchenie-sokrovishche",
]

# ----------------------------------------------------------------------------
# Вспомогательная графика
# ----------------------------------------------------------------------------
def lerp3(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def make_gradient(corners):
    """Многоточечный (4 угла) градиент, апскейленный до канвы."""
    gw, gh = 8, 8
    g = Image.new("RGB", (gw, gh))
    p = g.load()
    tl, tr, bl, br = corners
    for y in range(gh):
        fy = y / (gh - 1)
        for x in range(gw):
            fx = x / (gw - 1)
            p[x, y] = lerp3(lerp3(tl, tr, fx), lerp3(bl, br, fx), fy)
    return g.resize((W, H), Image.BICUBIC)


def add_blobs(img, blobs, color):
    """Мягкие размытые пятна-боке."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for cx, cy, r, a in blobs:
        d.ellipse((s(cx - r), s(cy - r), s(cx + r), s(cy + r)), fill=color + (a,))
    layer = layer.filter(ImageFilter.GaussianBlur(s(130)))
    img.alpha_composite(layer)


def add_rings(img, accent, rings):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for cx, cy, r, wd, a in rings:
        d.ellipse((s(cx - r), s(cy - r), s(cx + r), s(cy + r)),
                  outline=accent + (a,), width=max(1, s(wd)))
    layer = layer.filter(ImageFilter.GaussianBlur(s(1.4)))
    img.alpha_composite(layer)


def add_dots(img, accent, x0, y0, cols, rows, step, alpha=30, r=1.7):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for j in range(rows):
        for i in range(cols):
            cx, cy = x0 + i * step, y0 + j * step
            d.ellipse((s(cx - r), s(cy - r), s(cx + r), s(cy + r)), fill=accent + (alpha,))
    img.alpha_composite(layer)


def add_frame(img, accent, inset=44, radius=22, alpha=38):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle((s(inset), s(inset), W - s(inset), H - s(inset)),
                        radius=s(radius), outline=accent + (alpha,), width=max(1, s(1.5)))
    img.alpha_composite(layer)


def add_vignette(img, strength=0.50):
    img = img.convert("RGB")
    vw, vh = 240, 135
    v = Image.new("L", (vw, vh))
    p = v.load()
    for y in range(vh):
        ny = (y / (vh - 1)) * 2 - 1
        for x in range(vw):
            nx = (x / (vw - 1)) * 2 - 1
            d = math.sqrt(nx * nx * 0.92 + ny * ny)
            if d > 1.0:
                d = 1.0
            p[x, y] = int(255 * (1 - strength * d ** 2.1))
    v = v.resize((W, H), Image.BICUBIC).convert("RGB")
    return ImageChops.multiply(img, v)


def add_grain(img, sigma=24, amount=0.0):
    if amount <= 0:
        return img
    n = Image.effect_noise((W, H), sigma).convert("RGB")
    return Image.blend(img, ImageChops.overlay(img, n), amount)


# ----------------------------------------------------------------------------
# Текст
# ----------------------------------------------------------------------------
def wrap_text(text, font, max_w):
    words = text.split()
    lines, cur = [], ""
    for w in words:
        t = w if not cur else cur + " " + w
        if font.getlength(t) <= max_w or not cur:
            cur = t
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def tracked(draw, xy, text, font, fill, spacing=0.0, center_x=None, shadow=None):
    widths = [font.getlength(ch) for ch in text]
    total = sum(widths) + spacing * max(0, len(text) - 1)
    x = (center_x - total / 2.0) if center_x is not None else float(xy[0])
    y = float(xy[1])
    if shadow:
        off, col = shadow
        xr = x
        for ch, w in zip(text, widths):
            draw.text((xr + off, y + off), ch, font=font, fill=col)
            xr += w + spacing
    for ch, w in zip(text, widths):
        draw.text((x, y), ch, font=font, fill=fill)
        x += w + spacing
    return total


# ----------------------------------------------------------------------------
# Рендер одной картинки
# ----------------------------------------------------------------------------
def render(idx, author, quote, pal, tpl, seed):
    rnd = random.Random(seed)
    accent = pal["accent"]
    glow = pal["glow"]

    img = make_gradient(pal["bg"]).convert("RGBA")

    # --- фон: боке-пятна
    blobs = []
    for _ in range(3):
        blobs.append((rnd.uniform(120, 1800), rnd.uniform(90, 990),
                      rnd.uniform(300, 540), rnd.randint(58, 96)))
    add_blobs(img, blobs, glow)

    # --- декоративные окружности
    rings = [
        (rnd.uniform(1250, 1750), rnd.uniform(180, 420), rnd.uniform(300, 420), 1.4, 62),
        (rnd.uniform(1250, 1750), rnd.uniform(180, 420), rnd.uniform(430, 560), 1.4, 40),
    ]
    add_rings(img, accent, rings)

    # --- точечная текстура в правом верхнем углу
    add_dots(img, accent, 1596, 132, 7, 4, 34, alpha=34)

    # --- рамка
    add_frame(img, accent)

    def new_layer():
        return Image.new("RGBA", (W, H), (0, 0, 0, 0))

    # --- подбор кегля и перенос строк
    text = "\u00ab" + quote + "\u00bb"
    fs = 92
    max_text_w = 1080
    while True:
        qfont = F("quote", s(fs))
        lines = wrap_text(text, qfont, s(max_text_w))
        lh = int(s(fs) * 1.46)
        if lh * len(lines) <= s(600) or fs <= 32:
            break
        fs -= 2

    text_w = max(qfont.getlength(l) for l in lines)
    text_h = lh * len(lines)

    pad_x = 132 if tpl == 1 else 96
    pad_y = 86
    a_fs = 26
    afont = F("author", s(a_fs))
    sep_gap = 38
    sep_w = s(64)
    sep_h = s(3)
    author_h = int(s(a_fs) * 1.25)

    panel_w = int(text_w) + 2 * s(pad_x)
    panel_h = text_h + sep_gap + sep_h + sep_gap + author_h + 2 * s(pad_y)

    if tpl == 0:
        px, py = (W - panel_w) // 2, (H - panel_h) // 2 + s(4)
    elif tpl == 1:
        px, py = s(212), (H - panel_h) // 2
    elif tpl == 2:
        px, py = (W - panel_w) // 2 + s(34), (H - panel_h) // 2 + s(66)
    else:
        px, py = W - s(170) - panel_w, (H - panel_h) // 2 - s(26)

    center_align = (tpl == 0)
    text_left = px + s(pad_x)

    def line_x(lw):
        return int(px + (panel_w - lw) / 2) if center_align else text_left

    a_text = "\u2014 " + author.upper()
    a_sp = s(3.2)
    a_width = sum(afont.getlength(c) for c in a_text) + a_sp * (len(a_text) - 1)
    ax = int(px + (panel_w - a_width) / 2) if center_align else text_left
    sep_x = px + (panel_w - sep_w) // 2 if center_align else text_left
    sep_y = py + s(pad_y) + text_h + sep_gap
    author_y = sep_y + sep_h + sep_gap

    # --- крупная декоративная кавычка (под панелью)
    if tpl in (1, 2, 3):
        mk = {1: (1452, 736, 300, "\u201c", 32),
              2: (120, 0, 400, "\u201c", 48),
              3: (150, 688, 420, "\u201c", 48)}[tpl]
        lyr = new_layer()
        ImageDraw.Draw(lyr).text((s(mk[0]), s(mk[1])), mk[3],
                                 font=F("mark", s(mk[2])), fill=accent + (mk[4],))
        img.alpha_composite(lyr)

    # --- тень панели
    shadow = new_layer()
    ImageDraw.Draw(shadow).rounded_rectangle(
        (px, py + s(14), px + panel_w, py + panel_h + s(14)),
        radius=s(34), fill=(0, 0, 0, 165))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s(26))))

    # --- стеклянная панель (отдельный слой -> корректная альфа)
    glass = new_layer()
    ImageDraw.Draw(glass).rounded_rectangle(
        (px, py, px + panel_w, py + panel_h), radius=s(34),
        fill=(255, 255, 255, 30), outline=accent + (150,), width=max(1, s(1.6)))
    img.alpha_composite(glass)

    # --- мягкий блик сверху панели
    sw, sh = panel_w, max(1, int(panel_h * 0.62))
    grad = Image.new("L", (1, 256))
    for i in range(256):
        grad.putpixel((0, i), int(34 * (1 - i / 255.0) ** 1.7))
    grad = grad.resize((sw, sh), Image.BICUBIC)
    sheen = Image.new("L", (panel_w, panel_h), 0)
    sheen.paste(grad, (0, 0))
    clip = Image.new("L", (panel_w, panel_h), 0)
    ImageDraw.Draw(clip).rounded_rectangle((0, 0, panel_w - 1, panel_h - 1),
                                           radius=s(34), fill=255)
    sl = new_layer()
    sl.alpha_composite(Image.merge("RGBA", (
        Image.new("L", (panel_w, panel_h), 255),
        Image.new("L", (panel_w, panel_h), 255),
        Image.new("L", (panel_w, panel_h), 255),
        ImageChops.multiply(sheen, clip))), (0, 0))
    img.alpha_composite(sl, (px, py))

    # --- вертикальная акцентная полоса (шаблон 1)
    if tpl == 1:
        bx = px + s(46)
        lyr = new_layer()
        ImageDraw.Draw(lyr).rounded_rectangle(
            (bx, py + s(52), bx + s(6), py + panel_h - s(52)),
            radius=s(3), fill=accent + (240,))
        img.alpha_composite(lyr)

    # --- декоративная кавычка внутри панели (шаблон 0)
    if tpl == 0:
        lyr = new_layer()
        ImageDraw.Draw(lyr).text((px + s(24), py - s(54)), "\u201c",
                                 font=F("mark", s(250)), fill=accent + (100,))
        img.alpha_composite(lyr)

    # --- мягкая тень текста
    mask = Image.new("L", (W, H), 0)
    md = ImageDraw.Draw(mask)
    ty = py + s(pad_y)
    for line in lines:
        md.text((line_x(qfont.getlength(line)), ty), line, font=qfont, fill=255)
        ty += lh
    md.text((ax, author_y), a_text, font=afont, fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(s(3.4)))
    lyr = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    lyr.putalpha(mask.point(lambda v: int(v * 0.28)))
    img.alpha_composite(lyr, (0, s(3)))

    # --- текст: цитата, разделитель, автор
    tl = new_layer()
    td = ImageDraw.Draw(tl)
    text_col = (248, 245, 237, 255)
    ty = py + s(pad_y)
    for line in lines:
        td.text((line_x(qfont.getlength(line)), ty), line, font=qfont, fill=text_col)
        ty += lh
    td.rounded_rectangle((sep_x, sep_y, sep_x + sep_w, sep_y + sep_h),
                         radius=s(1.5), fill=accent + (240,))
    tracked(td, (0, author_y), a_text, afont, accent + (255,), spacing=a_sp,
            center_x=(ax + a_width / 2.0))
    img.alpha_composite(tl)

    # --- служебные подписи
    ll = new_layer()
    ld = ImageDraw.Draw(ll)
    lfont = F("label", s(15))
    tracked(ld, (0, H - s(88)),
            "МОТИВАЦИЯ  \u00b7  ОБУЧЕНИЕ",
            lfont, (255, 255, 255, 160), spacing=s(5), center_x=s(240))
    num = "%02d / %d" % (idx, TOTAL)
    nfont = F("label", s(17))
    nw = sum(nfont.getlength(c) for c in num) + s(4) * (len(num) - 1)
    tracked(ld, (W - s(120) - nw, H - s(90)), num, nfont, accent + (185,), spacing=s(4))
    img.alpha_composite(ll)

    # --- финал: виньетка, зерно, даунскейл
    img = add_vignette(img, 0.44)
    img = add_grain(img, 24, 0.07)
    return img.resize((DW, DH), Image.LANCZOS)


# ----------------------------------------------------------------------------
def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    files = []
    for i, (author, quote, pi) in enumerate(QUOTES, start=1):
        img = render(i, author, quote, PALETTES[pi], (i - 1) % 4, seed=1000 + i * 37)
        name = "%02d_%s.png" % (i, SLUGS[i - 1])
        path = os.path.join(OUT_DIR, name)
        img.convert("RGB").save(path, "PNG", optimize=True)
        files.append((name, author, quote))
        print("OK %02d/%02d  %s" % (i, TOTAL, name))

    # --- контактный лист
    cards = []
    for name, author, quote in files:
        cards.append(
            '<figure><img src="%s" alt="">'
            '<figcaption><b>%s</b><span>%s</span><code>%s</code></figcaption></figure>'
            % (html.escape(name), html.escape(author), html.escape(quote), html.escape(name))
        )
    doc = """<!DOCTYPE html>
<html lang="ru"><head><meta charset="utf-8">
<title>%d обоев 1920x1080 — мотивирующие цитаты об учении</title>
<style>
 :root{color-scheme:dark}
 *{box-sizing:border-box}
 body{margin:0;padding:48px 40px 80px;background:#0d0e13;color:#e9e7e1;
      font:16px/1.5 "Segoe UI",system-ui,sans-serif}
 h1{margin:0 0 6px;font-family:Georgia,serif;font-weight:400;font-size:34px;letter-spacing:.5px}
 p.sub{margin:0 0 36px;color:#8d8b86;font-size:15px}
 .grid{display:grid;gap:30px 26px;grid-template-columns:repeat(auto-fill,minmax(430px,1fr))}
 figure{margin:0;background:#15161c;border:1px solid #24262f;border-radius:14px;overflow:hidden;
        transition:transform .18s ease,border-color .18s ease}
 figure:hover{transform:translateY(-3px);border-color:#3a3d4a}
 img{display:block;width:100%%;height:auto;background:#000}
 figcaption{padding:14px 16px 16px}
 figcaption b{display:block;font-weight:600;font-size:14px;letter-spacing:.4px;color:#d9c58a;margin-bottom:6px}
 figcaption span{display:block;color:#b9b7b1;font-size:14px;min-height:42px}
 code{display:block;margin-top:10px;color:#6f7280;font:12px/1.4 Consolas,monospace}
</style></head><body>
<h1>%d обоев 1920&times;1080 &mdash; мотивирующие цитаты об учении</h1>
<p class="sub">PNG, 1920&times;1080. Правый клик по изображению &rarr; &laquo;Сохранить&raquo; или используйте файлы из этой папки.</p>
<div class="grid">
%s
</div></body></html>""" % (TOTAL, TOTAL, "\n".join(cards))

    with open(os.path.join(OUT_DIR, "index.html"), "w", encoding="utf-8") as f:
        f.write(doc)

    print("\nAll done -> %s" % OUT_DIR)


if __name__ == "__main__":
    main()
