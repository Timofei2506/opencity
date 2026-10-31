#!/usr/bin/env python3
"""
OpenCity - procedural PBR texture generator.

Generates a complete, seamless (tileable) PBR texture set for the game:
  *_d.png   albedo / base colour (sRGB)
  *_n.png   tangent-space normal map (linear)
  *_mg.png  metallic(R) / smoothness(A) packed map (linear)
  *_e.png   emissive map (sRGB)

Everything is deterministic (seeded), so the build is reproducible and the
repository stays free of third party assets.

Usage:  python3 Tools/gen_textures.py [--out Assets/Resources/Textures] [--size 512]
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# --------------------------------------------------------------------------------------
# low level noise helpers (periodic / tileable)
# --------------------------------------------------------------------------------------


def value_noise(size, freq, rng):
    """Periodic value noise on a size x size grid."""
    grid = rng.random((freq, freq))
    coord = np.linspace(0.0, freq, size, endpoint=False)
    i0 = np.floor(coord).astype(int)
    frac = coord - i0
    i1 = (i0 + 1) % freq
    smooth = frac * frac * (3.0 - 2.0 * frac)

    g00 = grid[np.ix_(i0, i0)]
    g10 = grid[np.ix_(i0, i1)]
    g01 = grid[np.ix_(i1, i0)]
    g11 = grid[np.ix_(i1, i1)]

    top = g00 * (1.0 - smooth)[None, :] + g10 * smooth[None, :]
    bot = g01 * (1.0 - smooth)[None, :] + g11 * smooth[None, :]
    return top * (1.0 - smooth)[:, None] + bot * smooth[:, None]


def fbm(size, freq, octaves, rng, gain=0.5, lacunarity=2.0):
    total = np.zeros((size, size))
    amp = 1.0
    f = max(1, int(freq))
    norm = 0.0
    for _ in range(octaves):
        total += amp * value_noise(size, f, rng)
        norm += amp
        amp *= gain
        f = max(1, int(round(f * lacunarity)))
    return total / norm


def ridged(size, freq, octaves, rng):
    return 1.0 - np.abs(fbm(size, freq, octaves, rng) * 2.0 - 1.0)


def normalize01(a):
    lo, hi = float(a.min()), float(a.max())
    if hi - lo < 1e-8:
        return np.zeros_like(a)
    return (a - lo) / (hi - lo)


def normal_from_height(height, strength=2.5):
    """Tileable tangent space normal map from a height field (0..1)."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    n = np.stack([-dx * strength, -dy * strength, np.ones_like(height)], axis=-1)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-6)
    return ((n * 0.5 + 0.5) * 255.0).astype(np.uint8)


def to_u8(rgb):
    return (np.clip(rgb, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def save_rgb(path, rgb):
    Image.fromarray(to_u8(rgb), "RGB").save(path, optimize=True)


def save_rgba(path, rgba):
    Image.fromarray((np.clip(rgba, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "RGBA").save(
        path, optimize=True
    )


def save_normal(path, height, strength=2.5):
    Image.fromarray(normal_from_height(height, strength), "RGB").save(path, optimize=True)


def save_mg(path, metallic, smoothness, size=None):
    """R = metallic, G = occlusion (unused by Unity Standard, kept 1), A = smoothness."""
    m = to_u8(np.asarray(metallic, dtype=float))
    s = to_u8(np.asarray(smoothness, dtype=float))
    if size is None:
        size = m.shape[0] if m.ndim == 2 else s.shape[0]
    if m.ndim == 0:
        m = np.full((size, size), float(m), dtype=np.uint8)
    if s.ndim == 0:
        s = np.full((size, size), float(s), dtype=np.uint8)
    if m.shape != s.shape:
        s = np.broadcast_to(s, m.shape).copy()
    occ = np.full_like(m, 255)
    img = np.stack([m, occ, np.zeros_like(m), s], axis=-1)
    Image.fromarray(img, "RGBA").save(path, optimize=True)


def downsample(img, size, mode=Image.LANCZOS):
    """Resize a float image (2D height/greyscale or 3D RGB) to size x size, keeping floats."""
    a = np.asarray(img, dtype=float)
    if a.ndim == 2:
        im = Image.fromarray(to_u8(a), "L").resize((size, size), mode)
        return np.asarray(im, dtype=float) / 255.0
    im = Image.fromarray(to_u8(a), "RGB").resize((size, size), mode)
    return np.asarray(im, dtype=float) / 255.0


def blur(a, radius):
    img = Image.fromarray(to_u8(a), "L").filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img, dtype=float) / 255.0


# --------------------------------------------------------------------------------------
# surfaces
# --------------------------------------------------------------------------------------


def tex_asphalt(size, rng, out):
    """Weathered city asphalt with aggregate, cracks and oil stains."""
    grain = fbm(size, 128, 4, rng)
    patches = fbm(size, 6, 4, rng)
    cracks = np.clip((ridged(size, 7, 7, rng) - 0.972) * 34.0, 0.0, 1.0) * 0.9

    base = 0.150 + 0.060 * patches + 0.070 * grain
    # aggregate speckles
    speck = (rng.random((size, size)) > 0.94).astype(float) * rng.random((size, size)) * 0.16
    base = base + speck
    # tar repair seams
    seam = np.clip(ridged(size, 3, 3, rng) - 0.93, 0, 1) * 8.0
    base = base * (1.0 - 0.45 * np.clip(seam, 0, 1))
    base = base * (1.0 - 0.35 * cracks)

    tint = np.stack([base * 1.0, base * 1.0, base * 1.03], axis=-1)
    save_rgb(os.path.join(out, "asphalt_d.png"), tint)

    height = 0.55 * grain + 0.25 * patches - 0.45 * cracks + 0.06 * speck * 4.0
    save_normal(os.path.join(out, "asphalt_n.png"), normalize01(height), 1.4)

    smoothness = 0.10 + 0.16 * patches - 0.10 * cracks
    metallic = np.zeros_like(base)
    save_mg(os.path.join(out, "asphalt_mg.png"), metallic, np.clip(smoothness, 0.03, 0.4))


def tex_sidewalk(size, rng, out):
    """Cast concrete paving slabs with joints."""
    grain = fbm(size, 96, 4, rng)
    patches = fbm(size, 8, 3, rng)
    slabs = 4
    img = np.zeros((size, size, 3))
    height = np.zeros((size, size))
    smooth = np.zeros((size, size))

    cell = size // slabs
    joint = max(2, size // 192)
    for sy in range(slabs):
        for sx in range(slabs):
            y0, y1 = sy * cell, (sy + 1) * cell
            x0, x1 = sx * cell, (sx + 1) * cell
            slab_rng = np.random.default_rng(int(rng.integers(1 << 30)) + sy * 31 + sx * 7)
            shade = 0.60 + slab_rng.random() * 0.10
            local = fbm(cell, 24, 3, np.random.default_rng(int(slab_rng.integers(1 << 30))))
            block = shade + 0.10 * local + 0.05 * grain[y0:y1, x0:x1]
            img[y0:y1, x0:x1, 0] = block * 1.00
            img[y0:y1, x0:x1, 1] = block * 0.995
            img[y0:y1, x0:x1, 2] = block * 0.975
            height[y0:y1, x0:x1] = 0.6 + 0.10 * local
            smooth[y0:y1, x0:x1] = 0.18 + 0.10 * local

    # joints + chamfer
    for k in range(slabs + 1):
        y = min(k * cell, size - 1)
        x = min(k * cell, size - 1)
        img[max(0, y - joint):y + 1, :, :] *= 0.55
        img[:, max(0, x - joint):x + 1, :] *= 0.55
        height[max(0, y - joint):y + 1, :] -= 0.30
        height[:, max(0, x - joint):x + 1] -= 0.30

    # stains and wear
    stain = normalize01(fbm(size, 4, 5, rng))
    img *= (0.86 + 0.20 * stain)[:, :, None]

    save_rgb(os.path.join(out, "sidewalk_d.png"), img)
    save_normal(os.path.join(out, "sidewalk_n.png"), normalize01(height), 2.2)
    save_mg(os.path.join(out, "sidewalk_mg.png"), np.zeros_like(img[:, :, 0]),
            np.clip(smooth + 0.08 * stain, 0.05, 0.5))


def tex_concrete(size, rng, out):
    grain = fbm(size, 110, 5, rng)
    blotch = normalize01(blur(fbm(size, 5, 4, rng), 3))
    streaks = normalize01(fbm(size, 2, 4, rng))
    streaks = np.array([np.roll(streaks, i, axis=0).mean(axis=0) for i in range(1)])[0]
    streak_field = np.tile(streaks[None, :], (size, 1))

    base = 0.50 + 0.10 * grain + 0.06 * blotch - 0.10 * streak_field
    img = np.stack([base, base * 0.995, base * 0.97], axis=-1)
    save_rgb(os.path.join(out, "concrete_d.png"), img)
    save_normal(os.path.join(out, "concrete_n.png"),
                normalize01(grain * 0.8 + blotch * 0.2), 1.1)
    save_mg(os.path.join(out, "concrete_mg.png"), np.zeros_like(base),
            np.clip(0.12 + 0.10 * grain, 0.05, 0.4))


def brick_pattern(size, rng, rows=16, cols=8, mortar=3):
    """Returns (albedo, height) for a running-bond brick wall."""
    img = np.zeros((size, size, 3))
    height = np.zeros((size, size))
    bh = size // rows
    bw = size // cols
    palette = np.array([
        [0.42, 0.20, 0.14], [0.36, 0.17, 0.12], [0.47, 0.24, 0.16],
        [0.33, 0.16, 0.13], [0.50, 0.28, 0.19], [0.39, 0.21, 0.15],
    ])
    for r in range(rows):
        offset = (bw // 2) if r % 2 else 0
        for c in range(-1, cols + 1):
            x0 = c * bw + offset
            y0 = r * bh
            x0m, y0m = int(x0), int(y0)
            cell = np.zeros((bh, bw, 3))
            col = palette[int(rng.integers(0, len(palette)))] * (0.85 + 0.3 * rng.random())
            local = fbm(max(bh, bw), 8, 3, rng)[:bh, :bw]
            for ch in range(3):
                cell[:, :, ch] = col[ch] * (0.85 + 0.30 * local)
            # mortar
            cell[:, :mortar, :] = 0.52
            cell[:mortar, :, :] = 0.52
            hcell = np.full((bh, bw), 0.85)
            hcell[:, :mortar] = 0.25
            hcell[:mortar, :] = 0.25
            # paste (wrap horizontally / vertically)
            for dy in range(bh):
                yy = (y0m + dy) % size
                for dx in range(bw):
                    xx = (x0m + dx) % size
                    if yy < size and xx < size:
                        img[yy, xx] = cell[dy, dx]
                        height[yy, xx] = hcell[dy, dx]
    return img, height


def tex_brick(size, rng, out):
    img, height = brick_pattern(size, rng, rows=max(8, size // 32), cols=max(4, size // 64))
    noise = fbm(size, 64, 4, rng)
    img *= (0.90 + 0.20 * noise)[:, :, None]
    save_rgb(os.path.join(out, "brick_d.png"), img)
    save_normal(os.path.join(out, "brick_n.png"), normalize01(height), 2.6)
    save_mg(os.path.join(out, "brick_mg.png"), np.zeros_like(img[:, :, 0]), 0.14)


def tex_grass(size, rng, out):
    coarse = fbm(size, 6, 4, rng)
    fine = fbm(size, 220, 3, rng)
    blades = ridged(size, 160, 2, rng)
    dry = normalize01(fbm(size, 3, 4, rng))
    dirt = np.clip(dry - 0.62, 0, 1) * 3.0
    base = np.stack([
        0.10 + 0.20 * coarse + 0.08 * fine + 0.22 * dirt,
        0.20 + 0.30 * coarse + 0.12 * fine + 0.05 * blades + 0.12 * dirt,
        0.05 + 0.08 * coarse + 0.04 * fine + 0.02 * dirt,
    ], axis=-1)
    save_rgb(os.path.join(out, "grass_d.png"), base)
    save_normal(os.path.join(out, "grass_n.png"),
                normalize01(fine * 0.7 + blades * 0.6), 2.0)
    save_mg(os.path.join(out, "grass_mg.png"), np.zeros_like(coarse), 0.08)


def tex_gravel_roof(size, rng, out):
    grain = fbm(size, 200, 4, rng)
    blotch = fbm(size, 7, 4, rng)
    base = 0.24 + 0.10 * grain + 0.08 * blotch
    img = np.stack([base * 1.02, base * 1.0, base * 0.97], axis=-1)
    save_rgb(os.path.join(out, "roof_d.png"), img)
    save_normal(os.path.join(out, "roof_n.png"), normalize01(grain), 2.0)
    save_mg(os.path.join(out, "roof_mg.png"), np.zeros_like(base), 0.09)


def tex_metal(size, rng, out):
    brushed = fbm(size, 260, 3, rng)
    wear = fbm(size, 10, 3, rng)
    base = 0.55 + 0.10 * brushed + 0.05 * wear
    img = np.stack([base, base, base * 1.02], axis=-1)
    save_rgb(os.path.join(out, "metal_d.png"), img)
    save_normal(os.path.join(out, "metal_n.png"), normalize01(brushed), 0.8)
    save_mg(os.path.join(out, "metal_mg.png"),
            np.clip(0.85 + 0.15 * wear, 0, 1), np.clip(0.45 + 0.25 * brushed, 0, 1))


def tex_tire(size, rng, out):
    tread = np.zeros((size, size))
    blocks = 24
    cell = size // blocks
    for by in range(blocks):
        off = (cell // 2) if by % 2 else 0
        for bx in range(-1, blocks + 1):
            y0 = by * cell
            x0 = ((bx * cell + off) % size)
            tread[y0 + 2:y0 + cell - 2, x0:x0 + cell - 3] = 1.0
    grain = fbm(size, 180, 3, rng)
    base = 0.045 + 0.03 * tread + 0.02 * grain
    img = np.stack([base, base, base], axis=-1)
    save_rgb(os.path.join(out, "tire_d.png"), img)
    save_normal(os.path.join(out, "tire_n.png"), normalize01(tread * 0.8 + grain * 0.2), 3.0)
    save_mg(os.path.join(out, "tire_mg.png"), np.zeros_like(tread), 0.22)


def tex_rim(size, rng, out):
    """Five-spoke alloy wheel face."""
    yy, xx = np.mgrid[0:size, 0:size]
    cx = cy = (size - 1) / 2.0
    dx = (xx - cx) / (size / 2.0)
    dy = (yy - cy) / (size / 2.0)
    r = np.sqrt(dx * dx + dy * dy)
    ang = np.arctan2(dy, dx)

    spokes = 5
    phase = np.mod(ang * spokes / (2 * np.pi) + 0.5, 1.0)
    spoke_half = 0.13 + 0.05 * np.clip(1.0 - r, 0, 1)
    spoke = (np.abs(phase - 0.5) < spoke_half) & (r > 0.20) & (r < 0.86)
    hub = r < 0.22
    lip = (r > 0.86) & (r < 0.98)
    tyre_well = r >= 0.98

    base = np.full((size, size), 0.18)
    base[spoke] = 0.62
    base[hub] = 0.70
    base[lip] = 0.78
    base[tyre_well] = 0.02
    shade = 0.90 + 0.14 * np.cos(ang * 2.0) * (r < 0.9)
    base = np.clip(base * shade, 0, 1)
    grime = normalize01(fbm(size, 24, 3, rng))
    base *= (0.86 + 0.18 * grime)

    save_rgb(os.path.join(out, "rim_d.png"), np.stack([base, base * 0.995, base * 0.985], axis=-1))

    height = np.full((size, size), 0.3)
    height[spoke] = 0.9
    height[hub] = 1.0
    height[lip] = 0.8
    height[tyre_well] = 0.1
    save_normal(os.path.join(out, "rim_n.png"), normalize01(height), 3.0)

    metallic = np.where(tyre_well, 0.0, 0.95)
    smooth = np.where(spoke | hub | lip, 0.82, 0.45)
    smooth = np.where(tyre_well, 0.1, smooth)
    save_mg(os.path.join(out, "rim_mg.png"), metallic, smooth)


def _noise_field(size, rng, freq, octaves=4):
    return fbm(size, freq, octaves, rng)


def facade_windows(size, rng, out, name, floors=4, cols=4, wall=(0.55, 0.55, 0.56),
                   lit_chance=0.30, brick=False, frame=(0.20, 0.20, 0.21),
                   glass=(0.10, 0.13, 0.16)):
    """A tiled facade unit: `floors` x `cols` windows with sills, frames and glass."""
    ss = 2  # supersampling factor
    S = size * ss
    wall_img = np.zeros((S, S, 3))
    height = np.zeros((S, S))
    emis = np.zeros((S, S, 3))
    smooth = np.zeros((S, S))
    metallic = np.zeros((S, S))

    # --- wall base -------------------------------------------------------------
    if brick:
        bi, bh = brick_pattern(S, rng, rows=max(8, S // 48), cols=max(4, S // 96))
        wall_img[:] = bi
        height[:] = bh * 0.5
    else:
        n1 = _noise_field(S, rng, 64)
        n2 = _noise_field(S, rng, 6)
        base = np.array(wall) * (0.86 + 0.24 * n1 + 0.10 * n2)[:, :, None]
        wall_img[:] = base
        height[:] = 0.5 + 0.05 * n1

    fh = S // floors
    cw = S // cols
    win_w = int(cw * 0.58)
    win_h = int(fh * 0.56)
    depth_shadow = max(2, S // 256)

    draw = ImageDraw.Draw(Image.fromarray(to_u8(wall_img), "RGB"))  # noqa: F841 (kept simple below)

    for f in range(floors):
        for c in range(cols):
            x0 = c * cw + (cw - win_w) // 2
            y0 = f * fh + (fh - win_h) // 2
            x1, y1 = x0 + win_w, y0 + win_h

            # recessed reveal (dark frame)
            wall_img[y0 - depth_shadow:y1 + depth_shadow, x0 - depth_shadow:x1 + depth_shadow] *= 0.72
            height[y0 - depth_shadow:y1 + depth_shadow, x0 - depth_shadow:x1 + depth_shadow] = 0.25

            # frame
            fr = max(2, S // 220)
            wall_img[y0:y1, x0:x1] = frame
            height[y0:y1, x0:x1] = 0.35
            smooth[y0:y1, x0:x1] = 0.45
            metallic[y0:y1, x0:x1] = 0.4

            # glass pane with vertical sky-reflection gradient
            gx0, gy0, gx1, gy1 = x0 + fr, y0 + fr, x1 - fr, y1 - fr
            gh = gy1 - gy0
            grad = np.linspace(0.55, 0.08, gh)[:, None]
            pane = np.zeros((gh, gx1 - gx0, 3))
            for ch in range(3):
                pane[:, :, ch] = glass[ch] * (0.4 + 1.5 * grad)
            refl = _noise_field(max(gh, gx1 - gx0, 8), rng, 6)[:gh, :gx1 - gx0]
            pane += refl[:, :, None] * 0.05
            wall_img[gy0:gy1, gx0:gx1] = np.clip(pane, 0, 1)
            smooth[gy0:gy1, gx0:gx1] = 0.92
            metallic[gy0:gy1, gx0:gx1] = 0.75
            height[gy0:gy1, gx0:gx1] = 0.30

            # occasional lit apartment (warm interior + curtain gradient)
            if rng.random() < lit_chance:
                warm = np.array([1.0, 0.78, 0.42]) * (0.55 + 0.45 * rng.random())
                curtain = np.linspace(0.35, 1.0, gh)[:, None, None]
                block = warm[None, None, :] * curtain
                # curtains only cover part of the window
                split = int((gx1 - gx0) * (0.2 + 0.5 * rng.random()))
                emis[gy0:gy1, gx0:gx0 + split] = block[:, 0:split, :] * 0.9
                wall_img[gy0:gy1, gx0:gx0 + split] = np.clip(
                    wall_img[gy0:gy1, gx0:gx0 + split] + block[:, 0:split, :] * 0.15, 0, 1)

            # sill + lintel
            sill = max(3, S // 150)
            wall_img[y1:y1 + sill, x0 - depth_shadow:x1 + depth_shadow] *= 1.18
            height[y1:y1 + sill, x0 - depth_shadow:x1 + depth_shadow] = 0.85
            wall_img[y0 - sill:y0, x0 - depth_shadow:x1 + depth_shadow] *= 0.80
            height[y0 - sill:y0, x0 - depth_shadow:x1 + depth_shadow] = 0.30

    # grime / weathering, stronger towards the bottom of the tile
    grime = _noise_field(S, rng, 5, 5)
    vert = np.linspace(0.0, 1.0, S)[:, None]
    wall_img *= (0.80 + 0.25 * grime * (0.6 + 0.6 * vert))[:, :, None]
    wall_img = np.clip(wall_img, 0, 1)

    def down(img, mode=Image.LANCZOS):
        return downsample(img, size, mode)

    save_rgb(os.path.join(out, f"{name}_d.png"), down(wall_img))
    save_normal(os.path.join(out, f"{name}_n.png"),
                normalize01(down(height)), 3.0)
    save_mg(os.path.join(out, f"{name}_mg.png"), down(metallic, Image.BILINEAR),
            down(smooth, Image.BILINEAR))
    em = down(emis)
    save_rgb(os.path.join(out, f"{name}_e.png"), em)


def facade_curtain_wall(size, rng, out, name="facade_glass", cols=4, rows=6):
    """Modern glass curtain wall: mullion grid, spandrel bands, lit offices."""
    ss = 2
    S = size * ss
    img = np.zeros((S, S, 3))
    emis = np.zeros((S, S, 3))
    height = np.zeros((S, S))
    smooth = np.zeros((S, S))
    metallic = np.zeros((S, S))

    cw = S // cols
    ch = S // rows
    mull = max(3, S // 170)
    for r in range(rows):
        for c in range(cols):
            x0, y0 = c * cw, r * ch
            x1, y1 = x0 + cw, y0 + ch
            spandrel = (r % 2 == 1) and (r != rows - 1)
            grad = np.linspace(0.62, 0.10, ch)[:, None]
            if spandrel:
                base = np.array([0.13, 0.14, 0.16])
                block = np.ones((ch, cw, 3)) * base[None, None, :]
                smooth[y0:y1, x0:x1] = 0.35
                metallic[y0:y1, x0:x1] = 0.3
                height[y0:y1, x0:x1] = 0.45
            else:
                tint = np.array([0.10, 0.16, 0.19]) * (0.8 + 0.4 * rng.random())
                vert = np.tile(grad.reshape(ch, 1), (1, cw))[:, :, None]
                block = tint[None, None, :] * (0.35 + 1.7 * vert)
                refl = _noise_field(max(ch, cw, 8), rng, 4)[:ch, :cw]
                block += refl[:, :, None] * 0.06
                smooth[y0:y1, x0:x1] = 0.95
                metallic[y0:y1, x0:x1] = 0.85
                height[y0:y1, x0:x1] = 0.30
                if rng.random() < 0.42 and not spandrel:
                    warm = np.array([1.0, 0.86, 0.60]) * (0.45 + 0.5 * rng.random())
                    ceil = np.linspace(1.0, 0.25, ch)[:, None, None]
                    emis[y0:y1, x0:x1] = warm[None, None, :] * ceil
            img[y0:y1, x0:x1] = np.clip(block, 0, 1)

            # aluminium mullions
            img[y0:y0 + mull, x0:x1] = 0.42
            img[y1 - mull:y1, x0:x1] = 0.34
            img[y0:y1, x0:x0 + mull] = 0.40
            img[y0:y1, x1 - mull:x1] = 0.32
            height[y0:y0 + mull, x0:x1] = 0.9
            height[y1 - mull:y1, x0:x1] = 0.9
            height[y0:y1, x0:x0 + mull] = 0.9
            height[y0:y1, x1 - mull:x1] = 0.9
            smooth[y0:y0 + mull, x0:x1] = 0.6
            smooth[y1 - mull:y1, x0:x1] = 0.6
            metallic[y0:y0 + mull, x0:x1] = 1.0
            metallic[y1 - mull:y1, x0:x1] = 1.0

    grime = _noise_field(S, rng, 6, 4)
    img *= (0.88 + 0.16 * grime)[:, :, None]

    def down(img, mode=Image.LANCZOS):
        return downsample(img, size, mode)

    save_rgb(os.path.join(out, f"{name}_d.png"), down(np.clip(img, 0, 1)))
    save_normal(os.path.join(out, f"{name}_n.png"),
                normalize01(down(height)), 3.0)
    save_mg(os.path.join(out, f"{name}_mg.png"), down(metallic, Image.BILINEAR),
            down(smooth, Image.BILINEAR))
    save_rgb(os.path.join(out, f"{name}_e.png"), down(emis))


# --------------------------------------------------------------------------------------
# vegetation / fx / ui helpers
# --------------------------------------------------------------------------------------


def tex_leaves(size, rng, out, name="leaves"):
    """Alpha-cutout canopy card: a cluster of leaf shapes."""
    ss = 2
    S = size * ss
    img = np.zeros((S, S, 4))
    height = np.zeros((S, S))
    # start from a soft blob mask so the card has volume
    blob = normalize01(blur(fbm(S, 4, 4, rng), S / 40.0))
    blob = np.clip((blob - 0.30) * 2.2, 0, 1)
    alpha = blob.copy()

    rng2 = np.random.default_rng(12345)
    for _ in range(int(S * 2.6)):
        cx = rng2.integers(0, S)
        cy = rng2.integers(0, S)
        if blob[cy, cx] < 0.35:
            continue
        rad = int(rng2.integers(max(2, S // 90), max(4, S // 34)))
        g = 0.18 + 0.35 * rng2.random()
        col = np.array([g * 0.55, g * 0.95, g * 0.35])
        yy, xx = np.mgrid[-rad:rad + 1, -rad:rad + 1]
        m = (xx ** 2 + yy ** 2 * 1.5) <= rad * rad
        ys = (cy + yy[m]) % S
        xs = (cx + xx[m]) % S
        img[ys, xs, :3] = col * (0.8 + 0.4 * rng2.random())
        img[ys, xs, 3] = 1.0
        height[ys, xs] += 0.4

    img[:, :, 3] *= alpha
    # keep a little translucency at the rim
    img[:, :, 3] = np.clip(img[:, :, 3] * 1.0, 0, 1)

    def down(a, mode=Image.LANCZOS):
        res = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8),
                              "RGBA" if a.shape[-1] == 4 else "RGB")
        return res.resize((size, size), mode)

    rgba = down(img)
    rgba.save(os.path.join(out, f"{name}_d.png"), optimize=True)
    save_normal(os.path.join(out, f"{name}_n.png"),
                normalize01(np.asarray(Image.fromarray(to_u8(height), "L").resize(
                    (size, size), Image.LANCZOS), dtype=float) / 255.0), 2.0)


def tex_soft_dot(size, out, name="soft_dot", core=0.0):
    yy, xx = np.mgrid[0:size, 0:size]
    cx = cy = (size - 1) / 2.0
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (size / 2.0)
    a = np.clip(1.0 - r, 0, 1) ** (2.0 + core * 6.0)
    rgba = np.zeros((size, size, 4))
    rgba[:, :, :3] = 1.0
    rgba[:, :, 3] = a
    save_rgba(os.path.join(out, f"{name}.png"), rgba)


def tex_circle_mask(size, out, name="circle_mask"):
    yy, xx = np.mgrid[0:size, 0:size]
    cx = cy = (size - 1) / 2.0
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (size / 2.0)
    a = np.clip((0.995 - r) * size * 0.5, 0, 1)
    rgba = np.zeros((size, size, 4))
    rgba[:, :, :3] = 1.0
    rgba[:, :, 3] = a
    save_rgba(os.path.join(out, f"{name}.png"), rgba)


def tex_ui_panel(size, out, name="ui_panel"):
    """Subtle vertical gradient used for HUD panels (white -> transparent-ish)."""
    grad = np.linspace(1.0, 0.55, size)[:, None]
    rgba = np.zeros((size, size, 4))
    for ch in range(3):
        rgba[:, :, ch] = 1.0
    rgba[:, :, 3] = np.clip(grad * 0.9, 0, 1)
    save_rgba(os.path.join(out, f"{name}.png"), rgba)


def tex_billboard(size, rng, out, name="billboard"):
    """Abstract advertising panel - abstract shapes + gradient, no text (locale safe)."""
    img = np.zeros((size, size, 3))
    hue = rng.random()
    c1 = np.array([0.9, 0.15 + 0.5 * hue, 0.2])
    c2 = np.array([0.1, 0.35 + 0.3 * hue, 0.85])
    grad = normalize01(np.add.outer(np.linspace(0, 1, size), np.linspace(0, 1, size)) / 2.0)
    img = c1[None, None, :] * grad[:, :, None] + c2[None, None, :] * (1 - grad[:, :, None])
    yy, xx = np.mgrid[0:size, 0:size]
    for k in range(5):
        cx = rng.integers(0, size)
        cy = rng.integers(0, size)
        rad = rng.integers(size // 12, size // 3)
        m = ((xx - cx) ** 2 + (yy - cy) ** 2) < rad * rad
        col = rng.random(3) * 0.8 + 0.2
        img[m] = img[m] * 0.35 + col[None, :] * 0.65
    noise = fbm(size, 40, 3, rng)
    img *= (0.92 + 0.16 * noise)[:, :, None]
    save_rgb(os.path.join(out, f"{name}_d.png"), np.clip(img, 0, 1))
    emis = np.clip(img * 1.05 - 0.15, 0, 1)
    save_rgb(os.path.join(out, f"{name}_e.png"), emis)


# --------------------------------------------------------------------------------------


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join("Assets", "Resources", "Textures"))
    ap.add_argument("--size", type=int, default=512)
    args = ap.parse_args()
    size = args.size
    out = args.out
    os.makedirs(out, exist_ok=True)

    rng = np.random.default_rng(20251031)
    print(f"[gen_textures] target={out} size={size}x{size}")

    steps = [
        ("asphalt", lambda: tex_asphalt(size, rng, out)),
        ("sidewalk", lambda: tex_sidewalk(size, rng, out)),
        ("concrete", lambda: tex_concrete(size, rng, out)),
        ("brick", lambda: tex_brick(size, rng, out)),
        ("grass", lambda: tex_grass(size, rng, out)),
        ("roof", lambda: tex_gravel_roof(size, rng, out)),
        ("metal", lambda: tex_metal(size, rng, out)),
        ("tire", lambda: tex_tire(size, rng, out)),
        ("rim", lambda: tex_rim(size, rng, out)),
        ("leaves", lambda: tex_leaves(size // 2, rng, out)),
        ("soft_dot", lambda: tex_soft_dot(64, out)),
        ("circle_mask", lambda: tex_circle_mask(256, out)),
        ("ui_panel", lambda: tex_ui_panel(32, out)),
        ("billboard", lambda: tex_billboard(size, rng, out)),
        ("facade_concrete", lambda: facade_windows(
            size, rng, out, "facade_concrete", floors=4, cols=4,
            wall=(0.56, 0.55, 0.52), lit_chance=0.28,
            frame=(0.22, 0.22, 0.23), glass=(0.09, 0.12, 0.15))),
        ("facade_brick", lambda: facade_windows(
            size, rng, out, "facade_brick", floors=5, cols=5,
            wall=(0.40, 0.21, 0.15), lit_chance=0.34, brick=True,
            frame=(0.30, 0.26, 0.24), glass=(0.08, 0.10, 0.13))),
        ("facade_panel", lambda: facade_windows(
            size, rng, out, "facade_panel", floors=6, cols=5,
            wall=(0.66, 0.64, 0.60), lit_chance=0.22,
            frame=(0.34, 0.34, 0.35), glass=(0.10, 0.14, 0.17))),
        ("facade_glass", lambda: facade_curtain_wall(size, rng, out)),
    ]

    total = 0
    for name, fn in steps:
        fn()
        print(f"  [ok] {name}")
    for f in sorted(os.listdir(out)):
        total += os.path.getsize(os.path.join(out, f))
    print(f"[gen_textures] {len(os.listdir(out))} files, {total / 1024.0 / 1024.0:.2f} MiB")


if __name__ == "__main__":
    sys.exit(main())
