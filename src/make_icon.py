# Genera icon.ico para Opti Hub: papel crema, velocímetro a tinta y florecita rosa (familia de Cursor Hub).
from PIL import Image, ImageDraw
import math, random

N = 1024
PAPER, INK, PINK, PINK_D = (245, 240, 232), (26, 18, 8), (234, 196, 196), (201, 154, 154)

def draw(size_hint):
    im = Image.new('RGBA', (N, N), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    pad, rad = 40, 210
    d.rounded_rectangle((pad, pad, N - pad, N - pad), rad, fill=PAPER)
    if size_hint >= 64:
        g = Image.new('RGBA', (N, N), (0, 0, 0, 0)); gp = g.load(); rnd = random.Random(7)
        for _ in range(26000):
            x, y = rnd.randrange(N), rnd.randrange(N); gp[x, y] = (70, 50, 20, rnd.randrange(10, 40))
        mask = Image.new('L', (N, N), 0); ImageDraw.Draw(mask).rounded_rectangle((pad, pad, N - pad, N - pad), rad, fill=255)
        im.paste(g, (0, 0), Image.composite(g, Image.new('RGBA', (N, N)), mask).split()[3])
    d.rounded_rectangle((pad, pad, N - pad, N - pad), rad, outline=INK, width=22 if size_hint >= 48 else 44)

    # arco del velocímetro
    cx, cy, R = 470, 600, 300
    w = 64 if size_hint >= 48 else 90
    d.arc((cx - R, cy - R, cx + R, cy + R), 180, 360, fill=INK, width=w)
    for a in (180, 225, 270, 315, 360):  # marcas
        if size_hint < 48: break
        t = math.radians(a)
        d.line((cx + math.cos(t) * (R - 70), cy + math.sin(t) * (R - 70), cx + math.cos(t) * (R - 125), cy + math.sin(t) * (R - 125)), fill=INK, width=30)
    # aguja hacia la zona alta
    t = math.radians(-38)
    d.line((cx, cy, cx + math.cos(t) * (R - 40), cy + math.sin(t) * (R - 40)), fill=INK, width=58 if size_hint >= 48 else 80)
    d.ellipse((cx - 62, cy - 62, cx + 62, cy + 62), fill=INK)

    # flor abajo a la derecha
    fx, fy, pr = 715, 735, 95 if size_hint >= 32 else 120
    for i in range(5):
        a = -math.pi / 2 + i * 2 * math.pi / 5
        px, py = fx + math.cos(a) * pr * 0.95, fy + math.sin(a) * pr * 0.95
        d.ellipse((px - pr * 0.72, py - pr * 0.72, px + pr * 0.72, py + pr * 0.72), fill=PINK, outline=PINK_D if size_hint >= 48 else None, width=14)
    d.ellipse((fx - pr * 0.42, fy - pr * 0.42, fx + pr * 0.42, fy + pr * 0.42), fill=INK)
    return im

sizes = [16, 24, 32, 48, 64, 128, 256]
imgs = [draw(s).resize((s, s), Image.LANCZOS) for s in sizes]
imgs[-1].save('icon.ico', format='ICO', sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save('../docs/icon.png')
print('ok')
