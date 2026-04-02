"""Generate Chrome extension icons for Unity Agent - Web Browser Bridge.

Produces 16x16, 48x48, and 128x128 PNGs with:
- Rounded square shape
- Purple-to-blue gradient (hydrangea/ajisai theme)
- Bridge/link motif representing Unity <-> Browser connection
"""

import os
from PIL import Image, ImageDraw, ImageFont

SIZES = [16, 48, 128]
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons")


def make_rounded_rect_mask(size: int, radius: int) -> Image.Image:
    """Create an alpha mask for a rounded rectangle."""
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def draw_gradient(img: Image.Image):
    """Fill image with a purple -> blue diagonal gradient."""
    w, h = img.size
    pixels = img.load()
    # Top-left: purple (138, 43, 226) -> Bottom-right: blue (30, 100, 220)
    c1 = (138, 43, 226)
    c2 = (30, 100, 220)
    for y in range(h):
        for x in range(w):
            # diagonal blend factor
            t = (x / w * 0.5 + y / h * 0.5)
            r = int(c1[0] + (c2[0] - c1[0]) * t)
            g = int(c1[1] + (c2[1] - c1[1]) * t)
            b = int(c1[2] + (c2[2] - c1[2]) * t)
            pixels[x, y] = (r, g, b, 255)


def draw_bridge_motif(draw: ImageDraw.Draw, size: int):
    """Draw a bridge/link motif — two interlocking chain links.

    Represents Unity <-> Browser connection. Uses two overlapping
    rounded rectangles (chain links) in white.
    """
    cx, cy = size / 2, size / 2
    s = size  # shorthand

    # Line width scales with icon size
    lw = max(1, int(s * 0.08))

    # Two overlapping chain-link ovals
    # Left link
    link_w = s * 0.32
    link_h = s * 0.22
    offset_x = s * 0.08

    left_box = [
        cx - link_w - offset_x,
        cy - link_h,
        cx + link_w * 0.15 - offset_x,
        cy + link_h,
    ]

    # Right link
    right_box = [
        cx - link_w * 0.15 + offset_x,
        cy - link_h,
        cx + link_w + offset_x,
        cy + link_h,
    ]

    r = max(1, int(link_h * 0.8))

    # Draw both links as rounded rectangle outlines
    draw.rounded_rectangle(left_box, radius=r, outline="white", width=lw)
    draw.rounded_rectangle(right_box, radius=r, outline="white", width=lw)

    # Small connecting dots at overlap center for "linked" feel
    dot_r = max(1, int(s * 0.03))
    draw.ellipse(
        [cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r],
        fill="white",
    )


def generate_icon(size: int) -> Image.Image:
    """Generate a single icon at the given size."""
    # Work at 4x resolution for anti-aliasing, then downscale
    work_size = size * 4
    img = Image.new("RGBA", (work_size, work_size), (0, 0, 0, 0))

    # Draw gradient background
    draw_gradient(img)

    # Apply rounded rectangle mask
    corner_radius = int(work_size * 0.2)
    mask = make_rounded_rect_mask(work_size, corner_radius)
    img.putalpha(mask)

    # Draw the bridge motif
    draw = ImageDraw.Draw(img)
    draw_bridge_motif(draw, work_size)

    # Downscale with high-quality resampling
    img = img.resize((size, size), Image.LANCZOS)
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for size in SIZES:
        icon = generate_icon(size)
        path = os.path.join(OUT_DIR, f"icon-{size}.png")
        icon.save(path, "PNG")
        print(f"Generated {path} ({size}x{size})")
    print("Done!")


if __name__ == "__main__":
    main()
