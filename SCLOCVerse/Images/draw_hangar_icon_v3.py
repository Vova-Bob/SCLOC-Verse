from PIL import Image, ImageDraw, ImageFilter
import math

SIZES = [64, 128, 256]
COLORS = {
    'primary': '#6DBBFF',
    'glow': '#8FD7FF',
    'dark': '#1A2430',
}

def hex_to_rgb(hex_color):
    return tuple(int(hex_color[i:i+2], 16) for i in (1, 3, 5))

def draw_icon(size, path):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    cx, cy = size // 2, size // 2
    s = size / 256.0

    primary = hex_to_rgb(COLORS['primary'])
    glow = hex_to_rgb(COLORS['glow'])
    dark = hex_to_rgb(COLORS['dark'])

    lw_main = max(2, int(3 * s))
    lw_thin = max(1, int(2 * s))
    lw_glow = max(2, int(5 * s))

    ring_r = int(95 * s)

    # 1. Subtle hangar contour behind (open bay mouth)
    hangar_w = int(156 * s)
    hangar_h = int(48 * s)
    hangar_y = cy + int(26 * s)
    hangar_points = [
        (cx - hangar_w // 2, hangar_y),
        (cx + hangar_w // 2, hangar_y),
        (cx + hangar_w // 2 + int(16*s), hangar_y + hangar_h),
        (cx - hangar_w // 2 - int(16*s), hangar_y + hangar_h),
    ]
    draw.polygon(hangar_points, fill=(*dark, 130), outline=(*primary, 90), width=lw_thin)

    # 2. Segmented HUD cycle ring (very thin, few segments)
    segments = 4
    gap = math.radians(18)
    for i in range(segments):
        a0 = math.radians(i * 360 / segments) + gap / 2
        a1 = math.radians((i + 1) * 360 / segments) - gap / 2
        # Glow
        draw.arc([cx - ring_r - lw_glow, cy - ring_r - lw_glow,
                  cx + ring_r + lw_glow, cy + ring_r + lw_glow],
                 start=math.degrees(a1), end=math.degrees(a0),
                 fill=(*glow, 60), width=lw_glow)
        # Main arc
        draw.arc([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
                 start=math.degrees(a1), end=math.degrees(a0),
                 fill=primary, width=lw_main)

    # 3. Top-down ship silhouette — clean delta
    ship_y = -int(4 * s)
    ship = [
        (cx, cy - int(58 * s) + ship_y),                      # nose
        (cx + int(34 * s), cy + int(16 * s) + ship_y),        # right wing
        (cx + int(16 * s), cy + int(34 * s) + ship_y),        # right engine
        (cx, cy + int(46 * s) + ship_y),                       # tail center
        (cx - int(16 * s), cy + int(34 * s) + ship_y),        # left engine
        (cx - int(34 * s), cy + int(16 * s) + ship_y),        # left wing
    ]
    draw.polygon(ship, fill=(*dark, 210), outline=primary, width=lw_main)

    # 4. Tiny HUD cockpit highlight
    cockpit = [
        (cx, cy - int(22 * s) + ship_y),
        (cx + int(8 * s), cy + int(6 * s) + ship_y),
        (cx, cy + int(16 * s) + ship_y),
        (cx - int(8 * s), cy + int(6 * s) + ship_y),
    ]
    draw.polygon(cockpit, fill=(*glow, 110), outline=(*glow, 180), width=lw_thin)

    # 5. Soft global glow
    glow_layer = img.filter(ImageFilter.GaussianBlur(radius=max(1, int(2 * s))))
    glow_layer = Image.blend(Image.new('RGBA', (size, size), (0,0,0,0)), glow_layer, 0.30)
    final = Image.alpha_composite(glow_layer, img)
    final.save(path, 'PNG')

for sz in SIZES:
    draw_icon(sz, f'F:/C#/SCLocalizationUA/SCLOCVerse/Images/hangar_icon_{sz}.png')
    print(f'Saved hangar_icon_{sz}.png')
