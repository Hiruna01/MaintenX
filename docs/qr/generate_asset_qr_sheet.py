"""Generates docs/qr/asset-qr-sheet.png — one printable QR sticker per seeded asset.

Each code encodes the asset tag and NOTHING else: `Asset.AssetTag` is the QR payload, and
the mobile scanner sends whatever it reads straight to GET /api/assets/by-tag/{assetTag}.
No URL, no prefix, no JSON — a sticker that carried more would need a parser on the phone,
and a parser is a second place for the contract to drift.

The tags are copied from api/Data/DbSeeder.cs. IF THE SEED CHANGES, CHANGE THEM HERE AND
REGENERATE — a sheet out of step with the seed is a demo that scans "No asset registered"
in front of the examiner.

    python -m venv .venv && .venv/bin/pip install "qrcode[pil]"
    .venv/bin/python docs/qr/generate_asset_qr_sheet.py

Prints on A4 at 300 DPI; print at 100% (not "fit to page") and each code is about 4 cm
square, comfortably readable by a phone camera from arm's length.
"""

from pathlib import Path

import qrcode
from PIL import Image, ImageDraw, ImageFont

# (tag, name, status) — verbatim from DbSeeder. The status is printed so the demo can
# point at the Retired and UnderMaintenance ones before scanning them.
SEEDED_ASSETS = [
    ("PRJ-MAB101-01", "Lecture Hall A Projector", "Active — repeat-failure history"),
    ("PRJ-MAB102-01", "Lecture Hall B Projector", "Active"),
    ("PRJ-MAB201-01", "Seminar Room 1 Projector", "Retired"),
    ("ACU-MAB101-01", "Lecture Hall A Split AC", "Active"),
    ("ACU-ENG101-01", "Computer Lab 1 Split AC", "Under maintenance"),
    ("PMP-ENG301-01", "Electronics Lab Water Pump", "Active"),
    ("WKS-ENG101-01", "Computer Lab 1 Workstation 01", "Active"),
    ("WKS-ENG102-01", "Computer Lab 2 Workstation 01", "Active"),
]

DPI = 300
PAGE_W, PAGE_H = round(8.27 * DPI), round(11.69 * DPI)  # A4
MARGIN = round(0.45 * DPI)
HEADER_H = round(0.75 * DPI)
COLUMNS, ROWS = 2, 4
QR_SIZE = round(1.55 * DPI)  # ~3.9 cm including the quiet zone

OUT = Path(__file__).with_name("asset-qr-sheet.png")

# The first font that exists wins, so the script runs on a macOS, Windows or Linux laptop.
SANS = [
    "/System/Library/Fonts/Helvetica.ttc",
    "C:/Windows/Fonts/arial.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
]
SANS_BOLD = [
    "/System/Library/Fonts/HelveticaNeue.ttc",
    "C:/Windows/Fonts/arialbd.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
]
MONO = [
    "/System/Library/Fonts/Menlo.ttc",
    "C:/Windows/Fonts/consola.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
]


def font(candidates: list[str], size: int, index: int = 0) -> ImageFont.FreeTypeFont:
    for path in candidates:
        if Path(path).exists():
            return ImageFont.truetype(path, size, index=index)
    return ImageFont.load_default(size=size)


def qr_image(payload: str) -> Image.Image:
    # Error correction M survives a scuffed or slightly glossy sticker. border=4 is the
    # quiet zone the QR spec requires; cropping it is the classic reason a code won't scan.
    code = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_M, border=4)
    code.add_data(payload)
    code.make(fit=True)
    image = code.make_image(fill_color="black", back_color="white").convert("RGB")
    return image.resize((QR_SIZE, QR_SIZE), Image.NEAREST)


def centred(draw: ImageDraw.ImageDraw, x_mid: int, y: int, text: str, f, fill) -> int:
    left, top, right, bottom = draw.textbbox((0, 0), text, font=f)
    draw.text((x_mid - (right - left) / 2, y - top), text, font=f, fill=fill)
    return y + (bottom - top)


def dashed_rect(draw: ImageDraw.ImageDraw, box, dash: int = 18, gap: int = 12) -> None:
    """Cut lines around each sticker."""
    x0, y0, x1, y1 = box
    colour = (190, 196, 206)
    for x in range(x0, x1, dash + gap):
        draw.line([(x, y0), (min(x + dash, x1), y0)], fill=colour, width=2)
        draw.line([(x, y1), (min(x + dash, x1), y1)], fill=colour, width=2)
    for y in range(y0, y1, dash + gap):
        draw.line([(x0, y), (x0, min(y + dash, y1))], fill=colour, width=2)
        draw.line([(x1, y), (x1, min(y + dash, y1))], fill=colour, width=2)


def main() -> None:
    page = Image.new("RGB", (PAGE_W, PAGE_H), "white")
    draw = ImageDraw.Draw(page)

    title_font = font(SANS_BOLD, 64, index=1)
    note_font = font(SANS, 34)
    tag_font = font(MONO, 58, index=1)
    name_font = font(SANS, 40)
    status_font = font(SANS, 32)
    brand_font = font(SANS_BOLD, 30, index=1)

    muted = (91, 102, 117)
    ink = (28, 36, 48)

    draw.text((MARGIN, MARGIN), "MaintenX — asset QR stickers", font=title_font, fill=ink)
    draw.text(
        (MARGIN, MARGIN + 88),
        "Demo seed (api/Data/DbSeeder.cs). Each code encodes only the asset tag. "
        "Print at 100% scale.",
        font=note_font,
        fill=muted,
    )

    grid_top = MARGIN + HEADER_H
    cell_w = (PAGE_W - 2 * MARGIN) // COLUMNS
    cell_h = (PAGE_H - grid_top - MARGIN) // ROWS

    for i, (tag, name, status) in enumerate(SEEDED_ASSETS):
        col, row = i % COLUMNS, i // COLUMNS
        x0 = MARGIN + col * cell_w
        y0 = grid_top + row * cell_h
        dashed_rect(draw, (x0, y0, x0 + cell_w, y0 + cell_h))

        x_mid = x0 + cell_w // 2
        y = y0 + 32
        y = centred(draw, x_mid, y, "MAINTENX · SCAN FOR SERVICE HISTORY", brand_font, (29, 78, 216))

        page.paste(qr_image(tag), (x_mid - QR_SIZE // 2, y + 16))
        y += 16 + QR_SIZE + 12

        # The tag in text as well, so a damaged sticker can still be typed into the app.
        y = centred(draw, x_mid, y, tag, tag_font, ink) + 18
        y = centred(draw, x_mid, y, name, name_font, ink) + 12
        centred(draw, x_mid, y, status, status_font, muted)

    page.save(OUT, dpi=(DPI, DPI))
    print(f"Wrote {OUT} ({PAGE_W}x{PAGE_H} px, {len(SEEDED_ASSETS)} codes)")


if __name__ == "__main__":
    main()
