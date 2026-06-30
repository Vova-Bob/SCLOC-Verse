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

def blend_with_alpha(base, overlay, alpha):
    return tuple(int(base[i] * (1 - alpha) + overlay[i] * alpha) for i in range(3))

def draw_icon(size, path):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    cx, cy = size // 2, size // 2
    scale = size / 256.0

    primary = hex_to_rgb(COLORS['primary'])
    glow = hex_to_rgb(COLORS['glow'])
    dark = hex_to_rgb(COLORS['dark'])
    bg = (14, 22, 30, 255)

    # Line weights tuned per size
    lw_main = max(2, int(3 * scale))
    lw_thin = max(1, int(2 * scale))
    lw_glow = max(2, int(5 * scale))

    # -----------------------------------------------------------------
    # 1. HANGAR BAY CONTOUR (wide trapezoid / landing pad behind)
    # -----------------------------------------------------------------
    hangar_w_top = int(176 * scale)
    hangar_w_bot = int(210 * scale)
    hangar_h = int(90 * scale)
    hangar_y_offset = int(28 * scale)
    hangar_points = [
        (cx - hangar_w_top // 2, cy - hangar_h // 2 + hangar_y_offset),
        (cx + hangar_w_top // 2, cy - hangar_h // 2 + hangar_y_offset),
        (cx + hangar_w_bot // 2, cy + hangar_h // 2 + hangar_y_offset),
        (cx - hangar_w_bot // 2, cy + hangar_h // 2 + hangar_y_offset),
    ]
    # Faint fill
    draw.polygon(hangar_points, fill=(*dark, 160))
    # Outer hangar lines
    draw.polygon(hangar_points, outline=(*primary, 120), width=lw_thin)
    # Inner floor line
    floor_y = cy + hangar_h // 2 + hangar_y_offset - int(8 * scale)
    draw.line([(cx - hangar_w_bot // 2 + int(10*scale), floor_y),
               (cx + hangar_w_bot // 2 - int(10*scale), floor_y)],
              fill=(*primary, 90), width=lw_thin)

    # -----------------------------------------------------------------
    # 2. SEGMENTED HUD CYCLE RING around ship
    # -----------------------------------------------------------------
    ring_r = int(92 * scale)
    segments = 8
    gap_deg = 6
    for i in range(segments):
        a0 = i * 360 // segments + gap_deg // 2
        a1 = (i + 1) * 360 // segments - gap_deg // 2
        # Glow arc
        draw.arc([cx - ring_r - lw_glow, cy - ring_r - lw_glow,
                  cx + ring_r + lw_glow, cy + ring_r + lw_glow],
                 start=a1, end=a0, fill=(*glow, 70), width=lw_glow)
        # Main arc
        draw.arc([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
                 start=a1, end=a0, fill=primary, width=lw_main)

    # -----------------------------------------------------------------
    # 3. TOP-DOWN SHIP SILHOUETTE (Star Citizen fighter style)
    # -----------------------------------------------------------------
    ship_y_offset = -int(4 * scale)
    s = scale
    ship_points = [
        (cx, cy - int(56 * s) + ship_y_offset),                    # nose
        (cx + int(16 * s), cy - int(24 * s) + ship_y_offset),      # right shoulder
        (cx + int(44 * s), cy + int(10 * s) + ship_y_offset),      # right wing tip
        (cx + int(22 * s), cy + int(18 * s) + ship_y_offset),     # right engine inner
        (cx + int(26 * s), cy + int(42 * s) + ship_y_offset),     # right engine rear
        (cx, cy + int(50 * s) + ship_y_offset),                    # tail center
        (cx - int(26 * s), cy + int(42 * s) + ship_y_offset),     # left engine rear
        (cx - int(22 * s), cy + int(18 * s) + ship_y_offset),     # left engine inner
        (cx - int(44 * s), cy + int(10 * s) + ship_y_offset),     # left wing tip
        (cx - int(16 * s), cy - int(24 * s) + ship_y_offset),     # left shoulder
    ]
    # Filled body
    draw.polygon(ship_points, fill=(*dark, 230), outline=primary, width=lw_main)
    # Inner detail lines (wings / hull seams)
    draw.line([(cx, cy - int(30*s) + ship_y_offset),
               (cx, cy + int(30*s) + ship_y_offset)],
              fill=(*glow, 140), width=lw_thin)
    draw.line([(cx - int(30*s), cy + int(8*s) + ship_y_offset),
               (cx - int(14*s), cy + int(8*s) + ship_y_offset)],
              fill=(*glow, 140), width=lw_thin)
    draw.line([(cx + int(14*s), cy + int(8*s) + ship_y_offset),
               (cx + int(30*s), cy + int(8*s) + ship_y_offset)],
              fill=(*glow, 140), width=lw_thin)
    # Cockpit glow
    cockpit = [
        (cx, cy - int(20*s) + ship_y_offset),
        (cx + int(8*s), cy + int(4*s) + ship_y_offset),
        (cx, cy + int(14*s) + ship_y_offset),
        (cx - int(8*s), cy + int(4*s) + ship_y_offset),
    ]
    draw.polygon(cockpit, fill=(*glow, 90), outline=(*glow, 160), width=lw_thin)

    # -----------------------------------------------------------------
    # 4. HUD BRACKETS / MARKERS
    # -----------------------------------------------------------------
    tick_r = ring_r + int(12 * scale)
    tick_len = int(12 * scale)
    for angle_deg in [45, 135, 225, 315]:
        a = math.radians(angle_deg)
        bx = cx + math.cos(a) * tick_r
        by = cy + math.sin(a) * tick_r
        # Short inward tick
        rdx = -math.cos(a) * tick_len
        rdy = -math.sin(a) * tick_len
        draw.line([(bx, by), (bx + rdx, by + rdy)], fill=(*primary, 160), width=lw_thin)

    # -----------------------------------------------------------------
    # 5. SUBTLE GLOBAL GLOW
    # -----------------------------------------------------------------
    glow_layer = img.filter(ImageFilter.GaussianBlur(radius=max(1, int(2 * scale))))
    glow_layer = Image.blend(Image.new('RGBA', (size, size), (0, 0, 0, 0)), glow_layer, 0.30)
    final = Image.alpha_composite(glow_layer, img)
    final.save(path, 'PNG')

for s in SIZES:
    draw_icon(s, f'F:/C#/SCLocalizationUA/SCLOCVerse/Images/hangar_icon_{s}.png')
    print(f'Saved hangar_icon_{s}.png')
