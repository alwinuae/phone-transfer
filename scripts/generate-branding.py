from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


def extract_mark(source: Image.Image) -> Image.Image:
    rgb = source.convert("RGB")
    candidate = Image.new("L", rgb.size)
    candidate.putdata(
        [
            0 if red >= 238 and green >= 238 and blue >= 238 else 255
            for red, green, blue in rgb.get_flattened_data()
        ]
    )

    for point in (
        (0, 0),
        (rgb.width - 1, 0),
        (0, rgb.height - 1),
        (rgb.width - 1, rgb.height - 1),
    ):
        ImageDraw.floodfill(candidate, point, 128)

    alpha = candidate.point(lambda value: 0 if value == 128 else 255)
    mark = rgb.convert("RGBA")
    mark.putalpha(alpha)
    bounds = alpha.getbbox()
    if bounds is None:
        raise RuntimeError("The logo mark could not be separated from its background.")
    return mark.crop(bounds)


def fit_mark(mark: Image.Image, size: int, fill: float) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    limit = int(size * fill)
    fitted = mark.copy()
    fitted.thumbnail((limit, limit), Image.Resampling.LANCZOS)
    x = (size - fitted.width) // 2
    y = (size - fitted.height) // 2
    canvas.alpha_composite(fitted, (x, y))
    return canvas


def rounded_android_icon(mark: Image.Image, size: int, circular: bool) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shape = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(shape)
    margin = int(size * 0.035)
    if circular:
        draw.ellipse((margin, margin, size - margin, size - margin), fill=255)
    else:
        radius = int(size * 0.22)
        draw.rounded_rectangle(
            (margin, margin, size - margin, size - margin),
            radius=radius,
            fill=255,
        )

    background = Image.new("RGBA", (size, size), (211, 232, 252, 255))
    canvas.paste(background, (0, 0), shape)
    fitted = fit_mark(mark, size, 0.78)
    canvas.alpha_composite(fitted)
    return canvas


def msix_asset(mark: Image.Image, width: int, height: int, fill: float) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (32, 34, 35, 255))
    limit = int(min(width, height) * fill)
    fitted = mark.copy()
    fitted.thumbnail((limit, limit), Image.Resampling.LANCZOS)
    canvas.alpha_composite(
        fitted,
        ((width - fitted.width) // 2, (height - fitted.height) // 2),
    )
    return canvas


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate Phone Transfer brand assets.")
    parser.add_argument("source", type=Path)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()

    root = args.root.resolve()
    with Image.open(args.source) as source:
        mark = extract_mark(source)

    desktop = fit_mark(mark, 1024, 0.92)
    desktop_512 = desktop.resize((512, 512), Image.Resampling.LANCZOS)
    desktop_path = root / "desktop/PhoneFolder.Desktop/Assets/PhoneTransfer.png"
    desktop_512.save(desktop_path, optimize=True)
    desktop_512.save(root / "assets/phone-transfer-logo.png", optimize=True)
    desktop_512.save(root / "assets/phone-transfer-logo-v2.png", optimize=True)
    desktop_512.save(
        root / "desktop/PhoneFolder.Desktop/Assets/PhoneTransfer.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )

    android_root = root / "android/app/src/main/res"
    rounded_android_icon(mark, 512, circular=False).save(
        android_root / "drawable-nodpi/ic_phonefolder.png",
        optimize=True,
    )
    rounded_android_icon(mark, 512, circular=False).save(
        android_root / "mipmap-nodpi/ic_launcher.png",
        optimize=True,
    )
    rounded_android_icon(mark, 512, circular=True).save(
        android_root / "mipmap-nodpi/ic_launcher_round.png",
        optimize=True,
    )
    fit_mark(mark, 432, 0.66).save(
        android_root / "drawable-nodpi/ic_launcher_foreground.png",
        optimize=True,
    )

    msix_root = root / "msix/Assets"
    msix_root.mkdir(parents=True, exist_ok=True)
    msix_asset(mark, 50, 50, 0.78).save(msix_root / "StoreLogo.png", optimize=True)
    msix_asset(mark, 44, 44, 0.76).save(
        msix_root / "Square44x44Logo.png",
        optimize=True,
    )
    msix_asset(mark, 150, 150, 0.78).save(
        msix_root / "Square150x150Logo.png",
        optimize=True,
    )
    msix_asset(mark, 310, 310, 0.78).save(
        msix_root / "Square310x310Logo.png",
        optimize=True,
    )
    msix_asset(mark, 310, 150, 0.76).save(
        msix_root / "Wide310x150Logo.png",
        optimize=True,
    )


if __name__ == "__main__":
    main()
