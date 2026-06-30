from PIL import Image, ImageDraw, ImageFilter
import math

SIZES = [64, 128, 256]
COLORS = {
    'primary': '#57C7FF',
    'secondary': '#AEEBFF',
    'shadow_dark': '#0E1620',
    'shadow_mid': '#1A2430',
}

def hex_to_rgb(hex_color):
    return tuple(int(hex_color[i:i+2], 16) for i in (1, 3, 5))

def draw_icon(size, path):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    cx, cy = size // 2, size // 2
    scale = size / 256.0

    primary = hex_to_rgb(COLORS['primary'])
    secondary = hex_to_rgb(COLORS['secondary'])
    shadow_dark = hex_to_rgb(COLORS['shadow_dark'])
    shadow_mid = hex_to_rgb(COLORS['shadow_mid'])

    # Line weights
    lw_outer = max(1, int(3 * scale))
    lw_inner = max(1, int(2 * scale))
    lw_glow = max(1, int(6 * scale))

    # Outer segmented cycle ring (HUD timer ring)
    ring_r = int(110 * scale)
    segments = 12
    gap = math.radians(8)
    for i in range(segments):
        a0 = math.radians(i * 360 / segments)
        a1 = math.radians((i + 1) * 360 / segments) - gap
        # Draw glow first
        draw.arc([cx - ring_r - lw_glow, cy - ring_r - lw_glow, cx + ring_r + lw_glow, cy + ring_r + lw_glow],
                 start=math.degrees(a1), end=math.degrees(a0),
                 fill=(*primary, 60), width=lw_glow)
        # Draw main arc
        draw.arc([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
                 start=math.degrees(a1), end=math.degrees(a0),
                 fill=primary, width=lw_outer)

    # Hangar bay contour (wide trapezoid mouth behind)
    hangar_w = int(170 * scale)
    hangar_top_h = int(55 * scale)
    hangar_bot_h = int(95 * scale)
    hangar_y_offset = int(20 * scale)
    hangar_points = [
        (cx - hangar_w // 2, cy - hangar_top_h // 2 + hangar_y_offset),
        (cx + hangar_w // 2, cy - hangar_top_h // 2 + hangar_y_offset),
        (cx + hangar_w // 2 + int(20*scale), cy + hangar_bot_h // 2 + hangar_y_offset),
        (cx - hangar_w // 2 - int(20*scale), cy + hangar_bot_h // 2 + hangar_y_offset),
    ]
    draw.polygon(hangar_points, fill=(*shadow_mid, 120), outline=(*primary, 80), width=lw_inner)

    # Ship silhouette (top-down delta)
    ship_y_offset = -int(10 * scale)
    ship_scale = scale
    ship_points = [
        (cx, cy - int(55 * ship_scale) + ship_y_offset),  # nose
        (cx + int(35 * ship_scale), cy + int(10 * ship_scale) + ship_y_offset),  # right wing rear
        (cx + int(18 * ship_scale), cy + int(25 * ship_scale) + ship_y_offset),  # right engine
        (cx + int(0 * ship_scale), cy + int(45 * ship_scale) + ship_y_offset),  # tail center
        (cx - int(18 * ship_scale), cy + int(25 * ship_scale) + ship_y_offset),  # left engine
        (cx - int(35 * ship_scale), cy + int(10 * ship_scale) + ship_y_offset),  # left wing rear
    ]
    draw.polygon(ship_points, fill=(*shadow_dark, 200), outline=secondary, width=lw_outer)
    # Inner cockpit highlight
    cockpit_points = [
        (cx, cy - int(25 * ship_scale) + ship_y_offset),
        (cx + int(10 * ship_scale), cy + int(5 * ship_scale) + ship_y_offset),
        (cx, cy + int(15 * ship_scale) + ship_y_offset),
        (cx - int(10 * ship_scale), cy + int(5 * ship_scale) + ship_y_offset),
    ]
    draw.polygon(cockpit_points, fill=(*primary, 120), outline=secondary, width=lw_inner)

    # HUD brackets (corner ticks)
    tick_len = int(18 * scale)
    tick_r = ring_r - int(12 * scale)
    for angle_deg in [30, 150, 210, 330]:
        angle = math.radians(angle_deg)
        bx = cx + math.cos(angle) * tick_r
        by = cy + math.sin(angle) * tick_r
        # Draw small bracket: radial tick + perpendicular tick
        rdx = math.cos(angle) * tick_len
        rdy = math.sin(angle) * tick_len
        perp = angle + math.radians(90)
        dx = math.cos(perp) * tick_len / 2
        dy = math.sin(perp) * tick_len / 2
        p1 = (bx, by)
        p2 = (bx + rdx, by + rdy)
        p3 = (bx + rdx + dx, by + rdy + dy)
        draw.line([p1, p2], fill=primary, width=lw_inner)
        draw.line([p2, p3], fill=primary, width=lw_inner)

    # Subtle overall glow via blur layer
    glow_layer = img.filter(ImageFilter.GaussianBlur(radius=max(1, int(2*scale))))
    glow_layer = Image.blend(Image.new('RGBA', (size, size), (0,0,0,0)), glow_layer, 0.35)
    final = Image.alpha_composite(glow_layer, img)
    final.save(path, 'PNG')

for s in SIZES:
    draw_icon(s, f'F:/C#/SCLocalizationUA/SCLOCVerse/Images/hangar_icon_{s}.png')
    print(f'Saved {s}x{s}')
