#!/usr/bin/env python3
"""Generate the original Shotline multi-resolution ICO; Python standard library only."""
from pathlib import Path
import math
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
SIZES = (16, 24, 32, 48, 64, 128, 256)


def rounded(x, y, left, top, width, height, radius):
    dx = max(left + radius - x, 0, x - (left + width - radius))
    dy = max(top + radius - y, 0, y - (top + height - radius))
    return left <= x <= left + width and top <= y <= top + height and dx * dx + dy * dy <= radius * radius


def triangle(x, y, points):
    def cross(a, b):
        return (x - b[0]) * (a[1] - b[1]) - (a[0] - b[0]) * (y - b[1])
    values = [cross(points[i], points[(i + 1) % 3]) for i in range(3)]
    return not (any(v < 0 for v in values) and any(v > 0 for v in values))


def pixel(x, y):
    if not rounded(x, y, 16, 16, 224, 224, 54):
        return (0, 0, 0, 0)
    color = (24, 61, 53, 255)
    if 24 <= x <= 232 and abs(y - (66 - 11 * ((x - 128) / 104) ** 2)) <= 2:
        color = (219, 191, 121, 255)
    angle = math.radians(7)
    px = 128 + (x - 128) * math.cos(angle) - (y - 142) * math.sin(angle)
    py = 142 + (x - 128) * math.sin(angle) + (y - 142) * math.cos(angle)
    if rounded(px, py, 56, 87, 147, 128, 10):
        color = (255, 249, 233, 255)
        if rounded(px, py, 64, 95, 131, 89, 4):
            color = (164, 199, 194, 255)
            if (px - 174) ** 2 + (py - 117) ** 2 < 81:
                color = (230, 177, 93, 255)
            if triangle(px, py, ((64, 184), (94, 139), (130, 184))) or triangle(px, py, ((90, 184), (146, 129), (195, 184))):
                color = (50, 121, 102, 255)
        if 70 <= px <= 135 and abs(py - 198) <= 1.5 or 70 <= px <= 108 and abs(py - 204) <= 1.5:
            color = (206, 214, 200, 255)
    if rounded(x, y, 123, 65, 13, 46, 4):
        color = (230, 177, 93, 255)
        if 126 <= x <= 133 and abs(y - 77) <= 1:
            color = (114, 87, 50, 255)
    return color


def png(size):
    scale = 4
    rows = bytearray()
    for y in range(size):
        rows.append(0)
        for x in range(size):
            samples = [pixel((x + (sx + 0.5) / scale) * 256 / size,
                             (y + (sy + 0.5) / scale) * 256 / size)
                       for sy in range(scale) for sx in range(scale)]
            alpha = sum(p[3] for p in samples)
            # Premultiplied averaging avoids a dark fringe around transparent edges.
            rows.extend([round(sum(p[c] * p[3] for p in samples) / alpha) if alpha else 0 for c in range(3)])
            rows.append(round(alpha / len(samples)))
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b'')


def main():
    images = [png(size) for size in SIZES]
    offset = 6 + len(SIZES) * 16
    entries = []
    for size, data in zip(SIZES, images):
        entries.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
        offset += len(data)
    destination = ROOT / 'src' / 'Shotline' / 'Assets' / 'Shotline.ico'
    destination.write_bytes(struct.pack('<HHH', 0, 1, len(SIZES)) + b''.join(entries) + b''.join(images))
    print(f'Generated {destination.name}: {len(SIZES)} sizes, {offset} bytes')


if __name__ == '__main__':
    main()
