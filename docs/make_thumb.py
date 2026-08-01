"""Workshop thumbnail for Sts2SkinSquad.

Type is the game's own: Slay the Spire 2 ships Kreon (res://fonts/kreon_bold.ttf), the same slab
serif the first game used, so a title set in it reads as part of the game rather than as a book
cover. The pck only carries rasterised .fontdata, not the source file, so the upstream OFL release
is used instead.

Weight is SemiBold rather than Bold, and the outline is thinner than the sister mods' — at 84px a
5px black rim is most of what made the old Georgia Bold version feel heavy and antique. The drop
shadow does the separation work instead, which keeps the letterforms open.
"""
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SRC = r"C:\Users\kl95\Desktop\새 폴더\스킨 스쿼드1.png"
OUT = r"C:\Users\kl95\sts2-card-advisor-dev\Sts2SkinSquad\thumbnail.png"
KREON = (r"C:\Users\kl95\AppData\Local\Temp\claude"
         r"\C--Users-kl95-sts2-card-advisor-dev\074cd139-67fc-416d-8400-d92b11db6255"
         r"\scratchpad\fonts\Kreon.ttf")

W, H = 960, 600
GOLD = (240, 208, 144)
TITLE = "SKIN SQUAD"
TAGLINE = "play solo"


def build(title_weight="SemiBold", tag_weight="Regular", out=OUT, tracking=6):
    src = Image.open(SRC).convert("RGBA")
    flat = Image.new("RGBA", src.size, (0, 0, 0, 255))
    flat.alpha_composite(src)
    src = flat.convert("RGB")

    # Trim the empty strip above the characters and the HP bar along the bottom: a stray "64/80"
    # behind the tagline reads as clutter, not as gameplay.
    src = src.crop((0, 34, src.width, 500))

    scale = max(W / src.width, H / src.height)
    back = src.resize((int(src.width * scale) + 1, int(src.height * scale) + 1), Image.LANCZOS)
    bx, by = (back.width - W) // 2, (back.height - H) // 2
    back = back.crop((bx, by, bx + W, by + H)).filter(ImageFilter.GaussianBlur(18))
    back = Image.blend(back, Image.new("RGB", (W, H), (8, 12, 10)), 0.45)

    fs = H / src.height
    fore = src.resize((int(src.width * fs), H), Image.LANCZOS)
    fx = (W - fore.width) // 2
    canvas = back.copy()
    canvas.paste(fore, (fx, 0))

    edge = Image.new("L", (W, H), 0)
    ed = ImageDraw.Draw(edge)
    FEATHER = 26
    for i in range(FEATHER):
        a = int(255 * (1 - i / FEATHER))
        ed.line([(fx + i, 0), (fx + i, H)], fill=a)
        ed.line([(fx + fore.width - 1 - i, 0), (fx + fore.width - 1 - i, H)], fill=a)
    canvas = Image.composite(back, canvas, edge)

    scrim = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sd = ImageDraw.Draw(scrim)
    BAND = 230
    for i in range(BAND):
        y = H - BAND + i
        sd.line([(0, y), (W, y)], fill=(4, 8, 6, int(245 * (i / BAND) ** 1.45)))
    canvas = Image.alpha_composite(canvas.convert("RGBA"), scrim)

    title_font = ImageFont.truetype(KREON, 82)
    title_font.set_variation_by_name(title_weight)
    tag_font = ImageFont.truetype(KREON, 29)
    tag_font.set_variation_by_name(tag_weight)

    def draw_tracked(layer, text, font, cy, fill, track):
        """Centred text with letter-spacing. Kreon is tight at display size; a little air is what
        makes a slab serif read as a logotype instead of as body copy."""
        d = ImageDraw.Draw(layer)
        widths = [d.textlength(ch, font=font) for ch in text]
        total = sum(widths) + track * (len(text) - 1)
        x = (W - total) / 2
        asc, desc = font.getmetrics()
        y = cy - (asc + desc) / 2
        for ch, w in zip(text, widths):
            d.text((x, y), ch, font=font, fill=fill)
            x += w + track

    def place(text, font, cy, fill, track, rim, blur, shadow_a):
        # Shadow first, on its own layer, so blurring it cannot smear the glyphs themselves.
        sh = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        draw_tracked(sh, text, font, cy + 3, (0, 0, 0, shadow_a), track)
        sh = sh.filter(ImageFilter.GaussianBlur(blur))
        canvas.alpha_composite(sh)

        if rim:
            rl = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            draw_tracked(rl, text, font, cy, (0, 0, 0, 210), track)
            canvas.alpha_composite(rl.filter(ImageFilter.MaxFilter(rim)))

        fg = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        draw_tracked(fg, text, font, cy, fill + (255,), track)
        canvas.alpha_composite(fg)

    place(TITLE, title_font, H - 126, GOLD, tracking, rim=3, blur=7, shadow_a=225)
    place(TAGLINE, tag_font, H - 56, (214, 200, 176), 3, rim=0, blur=5, shadow_a=205)

    canvas.convert("RGB").save(out, optimize=True)
    return out


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "compare":
        # Title-band crops of each weight, stacked, so the choice is made by looking rather than
        # by imagining what "SemiBold" means.
        strips = []
        for w in ("Medium", "SemiBold", "Bold"):
            tmp = build(title_weight=w, out=OUT.replace(".png", f".{w}.png"))
            im = Image.open(tmp).crop((0, H - 190, W, H - 20))
            lab = ImageDraw.Draw(im)
            lab.text((14, 8), f"Kreon {w}", fill=(255, 255, 255))
            strips.append(im)
        sheet = Image.new("RGB", (W, sum(s.height for s in strips)))
        y = 0
        for s in strips:
            sheet.paste(s, (0, y)); y += s.height
        sheet.save(OUT.replace("thumbnail.png", "docs/font_compare.png"))
        print("compare sheet saved")
    else:
        print("saved", build())
