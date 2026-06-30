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
    lw_detail = max(1, int(1 * s))

    ring_r = int(104 * s)

    # 1. Segmented HUD cycle ring (4 segments with small side ticks)
    segments = 4
    seg_deg = 82
    gap_deg = 8
    for i in range(segments):
        a0 = i * 90 - seg_deg // 2
        a1 = i * 90 + seg_deg // 2
        # Glow arc
        draw.arc([cx - ring_r - lw_glow, cy - ring_r - lw_glow,
                  cx + ring_r + lw_glow, cy + ring_r + lw_glow],
                 start=a1, end=a0, fill=(*glow, 70), width=lw_glow)
        # Main arc
        draw.arc([cx - ring_r, cy - ring_r, cx + ring_r, cy + ring_r],
                 start=a1, end=a0, fill=primary, width=lw_main)

    # Side HUD ticks near ring
    for angle in [0, 90, 180, 270]:
        a = math.radians(angle)
        r_in = ring_r - int(10 * s)
        r_out = ring_r + int(18 * s)
        x1 = cx + math.cos(a) * r_in
        y1 = cy + math.sin(a) * r_in
        x2 = cx + math.cos(a) * r_out
        y2 = cy + math.sin(a) * r_out
        draw.line([(x1, y1), (x2, y2)], fill=primary, width=lw_thin)
        # Small perpendicular cap
        px = -math.sin(a) * int(6 * s)
        py = math.cos(a) * int(6 * s)
        draw.line([(x2, y2), (x2 + px, y2 + py)], fill=primary, width=lw_thin)

    # 2. Hangar bay arch (octagonal, front view)
    arch_w_top = int(144 * s)
    arch_h = int(108 * s)
    arch_y_top = cy - int(10 * s)
    arch_y_bot = arch_y_top + arch_h
    chamfer = int(22 * s)
    roof_h = int(28 * s)
    arch_points = [
        (cx - arch_w_top // 2, arch_y_top + roof_h),           # left shoulder top
        (cx - arch_w_top // 2 - chamfer, arch_y_top + roof_h + chamfer), # left outer chamfer
        (cx - arch_w_top // 2 - chamfer, arch_y_bot - chamfer),          # left lower outer
        (cx - arch_w_top // 2, arch_y_bot),                  # left bottom
        (cx + arch_w_top // 2, arch_y_bot),                  # right bottom
        (cx + arch_w_top // 2 + chamfer, arch_y_bot - chamfer),          # right lower outer
        (cx + arch_w_top // 2 + chamfer, arch_y_top + roof_h + chamfer), # right outer chamfer
        (cx + arch_w_top // 2, arch_y_top + roof_h),         # right shoulder top
        (cx + int(40*s), arch_y_top),                         # right roof slope
        (cx, arch_y_top - int(12*s)),                          # roof peak
        (cx - int(40*s), arch_y_top),                          # left roof slope
    ]
    draw.polygon(arch_points, fill=(*dark, 180), outline=primary, width=lw_thin)

    # Inner hangar opening
    inner_w = int(108 * s)
    inner_h = int(78 * s)
    inner_y_top = arch_y_top + roof_h + int(8 * s)
    inner_y_bot = inner_y_top + inner_h
    inner_chamfer = int(14 * s)
    inner_points = [
        (cx - inner_w // 2, inner_y_top),
        (cx - inner_w // 2 - inner_chamfer, inner_y_top + inner_chamfer),
        (cx - inner_w // 2 - inner_chamfer, inner_y_bot - inner_chamfer),
        (cx - inner_w // 2, inner_y_bot),
        (cx + inner_w // 2, inner_y_bot),
        (cx + inner_w // 2 + inner_chamfer, inner_y_bot - inner_chamfer),
        (cx + inner_w // 2 + inner_chamfer, inner_y_top + inner_chamfer),
        (cx + inner_w // 2, inner_y_top),
    ]
    draw.polygon(inner_points, fill=(10, 16, 24, 230), outline=(*primary, 120), width=lw_thin)

    # Horizontal shadow lines inside hangar
    for i in range(1, 4):
        yy = inner_y_top + int(i * inner_h / 4)
        draw.line([(cx - inner_w // 2 + int(4*s), yy), (cx + inner_w // 2 - int(4*s), yy)],
                  fill=(*primary, 30), width=lw_detail)

    # Three small top lights
    light_w = int(6 * s)
    light_h = int(10 * s)
    light_y = arch_y_top + int(8 * s)
    for dx in [-int(12*s), 0, int(12*s)]:
        lx = cx + dx - light_w // 2
        draw.rectangle([lx, light_y, lx + light_w, light_y + light_h], fill=(*glow, 200))

    # 3. Ship silhouette (front view, standing on landing pad)
    ship_y = cy + int(18 * s)
    # Nose
    nose = [
        (cx, ship_y - int(46 * s)),
        (cx + int(10 * s), ship_y - int(18 * s)),
        (cx - int(10 * s), ship_y - int(18 * s)),
    ]
    # Fuselage
    body = [
        (cx + int(10 * s), ship_y - int(18 * s)),
        (cx + int(16 * s), ship_y + int(12 * s)),
        (cx + int(10 * s), ship_y + int(22 * s)),
        (cx - int(10 * s), ship_y + int(22 * s)),
        (cx - int(16 * s), ship_y + int(12 * s)),
        (cx - int(10 * s), ship_y - int(18 * s)),
    ]
    # Left wing
    left_wing = [
        (cx - int(10 * s), ship_y - int(8 * s)),
        (cx - int(48 * s), ship_y + int(4 * s)),
        (cx - int(42 * s), ship_y + int(14 * s)),
        (cx - int(10 * s), ship_y + int(8 * s)),
    ]
    # Right wing
    right_wing = [
        (cx + int(10 * s), ship_y - int(8 * s)),
        (cx + int(48 * s), ship_y + int(4 * s)),
        (cx + int(42 * s), ship_y + int(14 * s)),
        (cx + int(10 * s), ship_y + int(8 * s)),
    ]
    for poly in [left_wing, right_wing, body, nose]:
        draw.polygon(poly, fill=(*dark, 230), outline=primary, width=lw_thin)

    # Cockpit dark strip
    draw.rectangle([cx - int(4*s), ship_y - int(14*s), cx + int(4*s), ship_y + int(6*s)],
                    fill=(8, 14, 22, 230), outline=(*primary, 120), width=lw_detail)

    # Engine pods on wings
    for dx in [-int(38*s), int(38*s)]:
        ex = cx + dx
        ey = ship_y + int(12 * s)
        draw.ellipse([ex - int(5*s), ey - int(6*s), ex + int(5*s), ey + int(6*s)],
                     fill=(*glow, 160), outline=primary, width=lw_detail)

    # Landing gear legs
    gear_w = int(8 * s)
    gear_h = int(14 * s)
    for dx in [-int(10*s), int(10*s)]:
        gx = cx + dx - gear_w // 2
        draw.rectangle([gx, ship_y + int(22*s), gx + gear_w, ship_y + int(22*s) + gear_h],
                       fill=primary, outline=primary)

    # Landing pad
    pad_y = ship_y + int(36 * s)
    pad_w = int(108 * s)
    draw.line([(cx - pad_w // 2, pad_y), (cx + pad_w // 2, pad_y)],
              fill=primary, width=lw_thin)
    # Pad corner ticks
    tick = int(8 * s)
    draw.line([(cx - pad_w // 2, pad_y - tick), (cx - pad_w // 2, pad_y)], fill=primary, width=lw_detail)
    draw.line([(cx + pad_w // 2, pad_y - tick), (cx + pad_w // 2, pad_y)], fill=primary, width=lw_detail)

    # 4. Soft global glow
    glow_layer = img.filter(ImageFilter.GaussianBlur(radius=max(1, int(2 * s))))
    glow_layer = Image.blend(Image.new('RGBA', (size, size), (0,0,0,0)), glow_layer, 0.30)
    final = Image.alpha_composite(glow_layer, img)
    final.save(path, 'PNG')

for sz in SIZES:
    draw_icon(sz, f'F:/C#/SCLocalizationUA/SCLOCVerse/Images/hangar_icon_{sz}.png')
    print(f'Saved hangar_icon_{sz}.png')
