"""把 PNG 的指定区域裁剪并整数倍放大，用来检查像素细节。

用法：
    python tools/zoom_png.py 输入.png 输出.png x y w h [倍数]
"""
import sys
import struct
import zlib
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sample_png import read_png


def write_png(path, w, h, px):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        for x in range(w):
            i = (y * w + x) * 4
            raw += bytes((px[i], px[i + 1], px[i + 2], px[i + 3]))

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    out = b"\x89PNG\r\n\x1a\n"
    out += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    out += chunk(b"IDAT", zlib.compress(bytes(raw), 6))
    out += chunk(b"IEND", b"")
    open(path, "wb").write(out)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    x, y, w, h = (int(v) for v in sys.argv[3:7])
    scale = int(sys.argv[7]) if len(sys.argv) > 7 else 3

    sw, sh, ch, sp = read_png(src)
    out_w, out_h = w * scale, h * scale
    out = bytearray(out_w * out_h * 4)

    for j in range(out_h):
        sy = y + j // scale
        if sy >= sh:
            break
        for i in range(out_w):
            sx = x + i // scale
            if sx >= sw:
                continue
            si = (sy * sw + sx) * ch
            di = (j * out_w + i) * 4
            out[di] = sp[si]
            out[di + 1] = sp[si + 1]
            out[di + 2] = sp[si + 2]
            out[di + 3] = sp[si + 3] if ch == 4 else 255

    write_png(dst, out_w, out_h, out)
    print(f"{dst}  {out_w}x{out_h}（源 {src} 区域 {x},{y} {w}x{h} ×{scale}）")


if __name__ == "__main__":
    main()
