# Build-time helper (not shipped): finds the horn tip in each 512 px pose and prints the normalised (x, y) table used by
# HornTips.cs. The horn is the golden spiral at the upper right (side view, facing right). Method: mark "gold" pixels
# (opaque, hue 30-60 deg, saturation >= 0.35, value >= 0.55) in the top half of the picture, take the largest gold region
# (pixels up to 4 px apart joined) (the horn; the baked-in sparkles and the hooves are smaller or lower), and report its topmost pixel
# (ties: the rightmost), plus 0.5 to hit the pixel centre, divided by the picture size.
# Usage: python3 tools/measure-horn.py src/UnicornPet.App/Assets/poses
import colorsys, os, sys
from PIL import Image

POSES = ['idle', 'talk', 'happy_hop', 'walk_01', 'walk_02', 'walk_03', 'walk_04', 'sleep']

def gold(p):
    r, g, b, a = p
    if a < 200: return False
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return 30 / 360 <= h <= 60 / 360 and s >= 0.35 and v >= 0.55

R = 4   # pixels up to 4 apart count as touching, so the darker grooves between the horn's spiral bands do not split it

def tip(path):
    im = Image.open(path).convert('RGBA')
    w, h = im.size
    px = im.load()
    mask = {(x, y) for y in range(h // 2) for x in range(w) if gold(px[x, y])}
    best = []
    seen = set()
    for start in mask:
        if start in seen: continue
        comp, stack = [], [start]
        seen.add(start)
        while stack:
            x, y = stack.pop()
            comp.append((x, y))
            for dx in range(-R, R + 1):
                for dy in range(-R, R + 1):
                    q = (x + dx, y + dy)
                    if q in mask and q not in seen:
                        seen.add(q); stack.append(q)
        if len(comp) > len(best): best = comp
    x, y = min(best, key=lambda p: (p[1], -p[0]))
    return (x + 0.5) / w, (y + 0.5) / h, len(best), (x, y)

d = sys.argv[1] if len(sys.argv) > 1 else 'src/UnicornPet.App/Assets/poses'
for n in POSES:
    nx, ny, size, raw = tip(os.path.join(d, n + '.png'))
    print('%-10s x=%.4f y=%.4f  (pixel %s, horn region %d px)' % (n, nx, ny, raw, size))
