"""材质配色预览器：不启动引擎，直接用和 C# 相同的算法画出各种地面材质，
输出对比图（左：原始色；右：光照后）。用来快速调色。

用法：
    python tools/art_preview.py            # 输出 palette_preview.png
"""
import math
import struct
import zlib

# —————————————————————————— 调色板（与 Pal.cs 保持同步） ——————————————————————————

PAL = {}


def _hex(s):
    s = s.lstrip("#")
    return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16))


def load_palette():
    groups = {
        "G": ["#414A22", "#525E2A", "#667236", "#7C8A41", "#93A24D"],
        "D": ["#544731", "#68583E"],
        "R": ["#4C4234", "#615546", "#786A57", "#90806A", "#A79780"],
        "F": ["#3B2F1D", "#4D3E27", "#604E33", "#755F41", "#7E7439"],
        "H": ["#453928", "#564934", "#695A43", "#7C6C52"],
        "T": ["#222A2B", "#2E3839", "#3B4647", "#495556", "#586566"],
        "W": ["#241A10", "#352718", "#473521", "#5A432B"],
        "L": ["#1C2416", "#26321B", "#324223", "#40522C", "#506438", "#6E6531"],
        "S": ["#33302A", "#474238", "#5E584B", "#776F5F"],
        "A": ["#0B1518", "#122428", "#1B343A", "#2C4C52"],
    }
    for k, arr in groups.items():
        for i, v in enumerate(arr):
            PAL[f"{k}{i}"] = _hex(v)


load_palette()

UV = (0.020, 0.040, 0.055)   # ShadowDeep


# —————————————————————————— 噪声 ——————————————————————————

def h32(x, y, seed):
    h = (x * 374761393 + y * 668265263 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return h ^ (h >> 16)


def rand(x, y, seed=0):
    return h32(x, y, seed) / 4294967295.0


def noise(x, y, seed):
    xi, yi = math.floor(x), math.floor(y)
    xf, yf = x - xi, y - yi
    u = xf * xf * (3 - 2 * xf)
    v = yf * yf * (3 - 2 * yf)
    a = rand(xi, yi, seed)
    b = rand(xi + 1, yi, seed)
    c = rand(xi, yi + 1, seed)
    d = rand(xi + 1, yi + 1, seed)
    return (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v


def fbm(x, y, seed, oct=3):
    s = 0.0
    amp = 0.5
    norm = 0.0
    for i in range(oct):
        s += noise(x, y, seed + i * 977) * amp
        norm += amp
        x *= 2.03
        y *= 1.97
        amp *= 0.5
    return s / norm


# —————————————————————————— 画布 ——————————————————————————

class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [[(0, 0, 0, 0)] * w for _ in range(h)]

    def put(self, x, y, c, a=1.0):
        if not (0 <= x < self.w and 0 <= y < self.h):
            return
        if a >= 1.0:
            self.px[y][x] = (int(c[0]), int(c[1]), int(c[2]), 255)
            return
        dr, dg, db, da = self.px[y][x]
        sr, sg, sb = c
        oa = a * 255 + da * (1 - a)
        if oa <= 0:
            return
        nr = (sr * a * 255 + dr * da * (1 - a)) / oa
        ng = (sg * a * 255 + dg * da * (1 - a)) / oa
        nb = (sb * a * 255 + db * da * (1 - a)) / oa
        self.px[y][x] = (int(nr), int(ng), int(nb), int(oa))

    def get(self, x, y):
        return self.px[y][x]

    def rect(self, x, y, w, h, c):
        for j in range(h):
            for i in range(w):
                self.put(x + i, y + j, c)

    def disc(self, cx, cy, r, c, a=1.0):
        for y in range(int(cy - r), int(cy + r) + 2):
            for x in range(int(cx - r), int(cx + r) + 2):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.put(x, y, c, a)

    def disc_shade(self, cx, cy, r, c, a):
        for y in range(int(cy - r), int(cy + r) + 2):
            for x in range(int(cx - r), int(cx + r) + 2):
                d2 = (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2
                if d2 > r * r:
                    continue
                edge = 1 - math.sqrt(d2) / r
                self.put(x, y, c, a * min(1.0, edge * 2.2))


def darken(c, amt, cool=0.0):
    return (c[0] * (1 - amt),
            c[1] * (1 - amt) * (1 - cool * 0.30),
            c[2] * (1 - amt) * (1 + cool * 0.10))


def lighten(c, amt):
    return (min(255, c[0] * (1 + amt * 1.20)),
            min(255, c[1] * (1 + amt * 1.08)),
            min(255, c[2] * (1 + amt * 0.80)))


# —————————————————————————— 材质 ——————————————————————————

def paint_grass(cv, ox, oy, n=96):
    for j in range(n):
        for i in range(n):
            wx, wy = ox + i, oy + j
            nn = fbm(wx / 19.0, wy / 19.0, 101, 2)
            f = rand(wx, wy, 7)
            v = nn * 0.64 + f * 0.36
            if v < 0.25:
                c = PAL["D0"]
            elif v < 0.31:
                c = PAL["D1"]
            elif v < 0.44:
                c = PAL["G1"]
            elif v < 0.60:
                c = PAL["G2"]
            elif v < 0.76:
                c = PAL["G3"]
            else:
                c = PAL["G4"]
            cv.put(ox + i, oy + j, c)

    # 草叶簇
    for k in range(n * n // 260):
        bx = ox + 2 + int(rand(k, 1, 311) * (n - 4))
        by = oy + 2 + int(rand(k, 2, 312) * (n - 4))
        col = PAL["G4"] if rand(k, 3, 313) > 0.7 else PAL["G3"]
        cv.put(bx, by - 1, col)
        cv.put(bx - 1, by, col)
        cv.put(bx + 1, by, col)


def paint_road(cv, ox, oy, n=96):
    for j in range(n):
        for i in range(n):
            wx, wy = ox + i, oy + j
            nn = fbm(wx / 18.0, wy / 18.0, 211, 2)
            f = rand(wx, wy, 213)
            v = nn * 0.55 + f * 0.45
            c = PAL["R1"] if v < 0.38 else PAL["R2"] if v < 0.56 else PAL["R3"] if v < 0.74 else PAL["R4"]
            cv.put(ox + i, oy + j, c)
    for j in range(n):
        cv.put(ox + 5, oy + j, PAL["R0"], 0.16)
        cv.put(ox + 11, oy + j, PAL["R0"], 0.16)
    for k in range(n // 3):
        sx = ox + 1 + int(rand(k, 4, 230) * (n - 3))
        sy = oy + 1 + int(rand(k, 5, 231) * (n - 3))
        cv.put(sx, sy, PAL["S2"])


def paint_field(cv, ox, oy, n=96):
    for j in range(n):
        groove = j % 8
        if groove <= 1:
            c = PAL["F0"]
        elif groove == 2:
            c = PAL["F1"]
        elif groove <= 5:
            c = PAL["F2"]
        elif groove == 6:
            c = PAL["F3"]
        else:
            c = PAL["F2"]
        for i in range(n):
            f = rand(ox + i, oy + j, 301)
            cc = PAL["F3"] if f > 0.86 else PAL["F1"] if f < 0.14 else c
            cv.put(ox + i, oy + j, cc)


def paint_interior(cv, ox, oy, n=96):
    for j in range(n):
        for i in range(n):
            wx, wy = ox + i, oy + j
            nn = fbm(wx / 20.0, wy / 20.0, 401, 2)
            f = rand(wx, wy, 403)
            v = nn * 0.6 + f * 0.4
            c = PAL["H0"] if v < 0.42 else PAL["H1"] if v < 0.55 else PAL["H2"] if v < 0.72 else PAL["H3"]
            cv.put(ox + i, oy + j, c)
    # 四边墙根阴影
    for j in range(6):
        for i in range(n):
            cv.put(ox + i, oy + j, UV, 0.34 * (1 - j / 6.0))


def paint_wall(cv, ox, oy, n=96):
    bs = 6
    for j in range(n):
        for i in range(n):
            wx, wy = ox + i, oy + j
            row = wy // bs
            off = (row & 1) * 3
            bx = (wx + off) // bs
            r = rand(bx, row, 701)
            base = PAL["S0"] if r < 0.22 else PAL["S1"] if r < 0.62 else PAL["S2"]

            lx = (wx + off) % bs
            ly = wy % bs
            if lx == 0 or ly == 0:
                c = darken(base, 0.38, 0.15)
            elif lx == 1 or ly == 1:
                c = lighten(base, 0.12)
            else:
                c = base

            # 石块上的坑洼与苔痕，别太均匀
            g = rand(wx, wy, 707)
            if g > 0.90:
                c = lighten(base, 0.08)
            elif g < 0.05:
                c = darken(base, 0.16, 0.2)
            if rand(wx // 7, wy // 7, 709) > 0.90:
                c = PAL["L1"]

            cv.put(ox + i, oy + j, c)

    # 南立面：墙的厚度
    face = 6
    for j in range(n - face, n):
        row = j - (n - face)
        for i in range(n):
            wx = ox + i
            col = (wx + (row // 2) * 2) // 5
            shade = rand(col, row // 2, 811)
            c = PAL["S0"] if shade < 0.33 else PAL["S1"] if shade < 0.70 else PAL["S2"]
            if j == n - face:
                c = darken(c, 0.42, 0.3)
            cv.put(wx, oy + j, c)
    for i in range(n):
        cv.put(ox + i, oy + n - face - 1, PAL["S3"], 0.6)


def paint_tree(cv, ox, oy, n=96):
    cx, cy = ox + n / 2, oy + n / 2
    cv.disc_shade(cx + 1, cy + 30, 15, UV, 0.34)

    # 树干
    for y in range(int(cy - 6), int(cy + 34)):
        cv.put(int(cx) - 2, y, PAL["W0"])
        cv.put(int(cx) - 1, y, PAL["W1"])
        cv.put(int(cx), y, PAL["W2"])
        cv.put(int(cx) + 1, y, PAL["W0"])

    # 树冠：几团重叠的圆，不是同心圆
    blobs = [
        (0, 4, 20, "L0"), (-6, 0, 16, "L1"), (7, 2, 15, "L1"),
        (-2, -8, 16, "L1"), (-10, -4, 11, "L2"), (5, -10, 12, "L2"),
        (-6, -12, 9, "L3"), (2, -16, 8, "L3"), (-13, -10, 6, "L4"),
        (-8, -18, 5, "L4"),
    ]
    for dx, dy, r, key in blobs:
        cv.disc(cx + dx, cy + dy, r, PAL[key])

    # 边缘毛刺
    import math as _m
    for a in range(72):
        ang = a / 72.0 * _m.tau
        rad = 21 + rand(a, 0, 61) * 4
        ex = int(cx + _m.cos(ang) * rad)
        ey = int(cy + _m.sin(ang) * rad)
        if rand(a, 1, 62) > 0.45:
            cv.put(ex, ey, PAL["L0"])

    # 黄叶
    for i in range(10):
        lx = int(cx + (rand(i, 2, 63) - 0.5) * 32)
        ly = int(cy + (rand(i, 3, 64) - 0.5) * 30)
        cv.put(lx, ly, PAL["L5"])


# —————————————————————————— 光照 ——————————————————————————

def grade(cv):
    cx, cy = cv.w / 2, cv.h / 2
    max_d = math.hypot(cx, cy)
    for y in range(cv.h):
        for x in range(cv.w):
            r, g, b, a = cv.px[y][x]
            if a == 0:
                continue
            lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
            wr, wg, wb = 1.045, 1.000, 0.930
            t = max(0.0, min(1.0, 1 - lum / 0.36))
            wr -= t * 0.050
            wg += t * 0.020
            wb += t * 0.115
            dx, dy = (x - cx) / max_d, (y - cy) / max_d
            d = math.hypot(dx, dy)
            vig = 1 - 0.30 * d * d * 1.9
            cv.px[y][x] = (int(min(255, r * wr * vig)),
                           int(min(255, g * wg * vig)),
                           int(min(255, b * wb * vig)), a)


# —————————————————————————— PNG 输出 ——————————————————————————

def write_png(path, cv):
    raw = bytearray()
    for y in range(cv.h):
        raw.append(0)
        for x in range(cv.w):
            r, g, b, a = cv.px[y][x]
            raw += bytes((int(r), int(g), int(b), int(a)))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", cv.w, cv.h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")
    open(path, "wb").write(png)


def main():
    cell = 96
    gap = 8
    mats = [paint_grass, paint_road, paint_field, paint_interior, paint_wall, paint_tree]

    cols, rows = 2, 3
    group_w = cols * cell + (cols - 1) * gap
    group_h = rows * cell + (rows - 1) * gap

    cv = Canvas(group_w * 2 + 24 + 16, group_h + 16)
    cv.rect(0, 0, cv.w, cv.h, (18, 18, 18))

    for gi, do_grade in enumerate([False, True]):
        gx0 = 8 + gi * (group_w + 24)
        for mi, fn in enumerate(mats):
            mx = gx0 + (mi % cols) * (cell + gap)
            my = 8 + (mi // cols) * (cell + gap)
            sub = Canvas(cell, cell)
            fn(sub, 0, 0, cell)
            if do_grade:
                grade(sub)
            for y in range(cell):
                for x in range(cell):
                    cv.px[my + y][mx + x] = sub.px[y][x]

    write_png("palette_preview.png", cv)
    print(f"已写出 palette_preview.png  {cv.w}x{cv.h}")
    print("左：原始色　右：光照后")
    for name, key in [("草地 G2", "G2"), ("土路 R3", "R3"), ("荒田 F2", "F2"),
                      ("室内 H2", "H2"), ("墙 S2", "S2"), ("树 L3", "L3")]:
        c = PAL[key]
        print(f"  {name:8} #{c[0]:02X}{c[1]:02X}{c[2]:02X}  R={c[0]:3} G={c[1]:3} B={c[2]:3}")


if __name__ == "__main__":
    main()
