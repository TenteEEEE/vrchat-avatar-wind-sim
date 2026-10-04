from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SHEET = Path(__file__).resolve().with_name("sheet.png")
ICON_DIR = ROOT / "Packages" / "com.tentee.vrc-kazamachi" / "Icons"
PREVIEW = ROOT / "_work" / "icons-preview.png"
NAMES = ("Kazamachi", "Wind", "Direction", "Strength", "Turbulence", "Elevation")
BLACK_POINT = 12
TARGET_SIDE = 200


def make_icon(cell: Image.Image) -> Image.Image:
    gray = cell.convert("L")
    alpha = gray.point(lambda value: 0 if value <= BLACK_POINT else round((value - BLACK_POINT) * 255 / (255 - BLACK_POINT)))
    bbox = alpha.getbbox()
    if bbox is None:
        raise ValueError("empty icon cell")
    alpha = alpha.crop(bbox)
    scale = TARGET_SIDE / max(alpha.size)
    size = (max(1, round(alpha.width * scale)), max(1, round(alpha.height * scale)))
    alpha = alpha.resize(size, Image.Resampling.LANCZOS)
    square = Image.new("L", (256, 256), 0)
    square.paste(alpha, ((256 - size[0]) // 2, (256 - size[1]) // 2))
    rgba = Image.new("RGBA", (256, 256), (255, 255, 255, 0))
    rgba.putalpha(square)
    return rgba


def main() -> None:
    sheet = Image.open(SHEET).convert("RGB")
    width, height = sheet.size
    icons = []
    for index, name in enumerate(NAMES):
        column = index % 3
        row = index // 3
        left, right = round(width * column / 3), round(width * (column + 1) / 3)
        top, bottom = round(height * row / 2), round(height * (row + 1) / 2)
        icon = make_icon(sheet.crop((left, top, right, bottom)))
        icon.save(ICON_DIR / f"{name}.png", format="PNG")
        icons.append((name, icon))

    preview = Image.new("RGB", (3 * 300, 2 * 390), "#303030")
    draw = ImageDraw.Draw(preview)
    try:
        font = ImageFont.truetype("arial.ttf", 20)
    except OSError:
        font = ImageFont.load_default()
    for index, (name, icon) in enumerate(icons):
        col, row = index % 3, index // 3
        x, y = col * 300, row * 390
        preview.paste(icon, (x + 22, y + 24), icon)
        preview.paste(icon.resize((64, 64), Image.Resampling.LANCZOS), (x + 118, y + 275), icon.resize((64, 64), Image.Resampling.LANCZOS))
        draw.text((x + 22, y + 350), name, fill="white", font=font)
    preview.save(PREVIEW, format="PNG")


if __name__ == "__main__":
    main()
