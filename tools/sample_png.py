"""开发辅助：读 PNG 并采样指定坐标的颜色。没装 PIL，所以自己解 PNG。

用法：
    python tools/sample_png.py bake.png 152 254 200 254 ...
    会打印每个坐标的十六进制色值。
"""
import sys
import zlib
import struct


def read_png(path):
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "not a png"
    pos = 8
    idat = b""
    w = h = ct = 0
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        typ = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + ln]
        if typ == b"IHDR":
            w, h, bd, ct, comp, filt, inter = struct.unpack(">IIBBBBB", chunk)
        elif typ == b"IDAT":
            idat += chunk
        elif typ == b"IEND":
            break
        pos += 12 + ln

    raw = zlib.decompress(idat)
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ct]
    stride = w * ch
    out = bytearray(h * stride)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        f = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if f == 1:
            for i in range(ch, stride):
                line[i] = (line[i] + line[i - ch]) & 255
        elif f == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 255
        elif f == 3:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 255
        elif f == 4:
            for i in range(stride):
                a = line[i - ch] if i >= ch else 0
                b = prev[i]
                c = prev[i - ch] if i >= ch else 0
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 255
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, ch, out


def main():
    path = sys.argv[1]
    w, h, ch, px = read_png(path)
    print(f"{path}  {w}x{h}  channels={ch}")
    args = sys.argv[2:]
    for i in range(0, len(args), 2):
        x, y = int(args[i]), int(args[i + 1])
        if not (0 <= x < w and 0 <= y < h):
            print(f"  ({x},{y}) 越界")
            continue
        idx = (y * w + x) * ch
        r, g, b = px[idx], px[idx + 1], px[idx + 2]
        print(f"  ({x:3},{y:3})  #{r:02X}{g:02X}{b:02X}   R={r:3} G={g:3} B={b:3}")


if __name__ == "__main__":
    main()
