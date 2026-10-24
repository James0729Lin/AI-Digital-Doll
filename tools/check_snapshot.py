"""Fails when a snapshot PNG is missing, has no transparent background or shows no character."""
import struct
import sys
import zlib


def read_rgba(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG file")
    pos, idat, width, height, color_type = 8, b"", 0, 0, 0
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, color_type = struct.unpack(">IIBB", chunk[:10])
            if depth != 8 or color_type != 6:
                raise ValueError(f"expected 8-bit RGBA, got depth={depth} color_type={color_type}")
        elif kind == b"IDAT":
            idat += chunk
        pos += 12 + length
    raw = zlib.decompress(idat)
    stride = width * 4
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(height):
        filter_type, line = raw[i], bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - 4] if x >= 4 else 0
            b = prev[x]
            c = prev[x - 4] if x >= 4 else 0
            if filter_type == 1:
                line[x] = (line[x] + a) & 0xFF
            elif filter_type == 2:
                line[x] = (line[x] + b) & 0xFF
            elif filter_type == 3:
                line[x] = (line[x] + (a + b) // 2) & 0xFF
            elif filter_type == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 0xFF
        rows.append(bytes(line))
        prev = line
    return width, height, rows


def main(path):
    width, height, rows = read_rgba(path)
    opaque = sum(1 for row in rows for x in range(3, len(row), 4) if row[x] > 200)
    coverage = opaque / (width * height)
    corner_alpha = rows[0][3]
    print(f"{path}: {width}x{height}, character covers {coverage:.1%}, corner alpha {corner_alpha}")
    if corner_alpha != 0:
        sys.exit("background is not transparent")
    if not 0.05 < coverage < 0.9:
        sys.exit("character not visible in the snapshot")


if __name__ == "__main__":
    main(sys.argv[1])
