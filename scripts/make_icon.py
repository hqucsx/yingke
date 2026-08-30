#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成拓 Ta 应用图标：蓝色渐变圆角方块 + 白色"拓"字 + 截图选框四角。
输出多尺寸 ICO（256/128/64/48/32/16）。"""
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

SIZE = 1024          # 渲染画布（4x 超采样后缩到 256）
OUT = 256            # 主尺寸
RADIUS = 230         # 圆角半径
BLUE_TOP = (91, 145, 255)    # #5B91FF
BLUE_BOT = (43, 91, 224)     # #2B5BE0
WHITE = (255, 255, 255)
FRAME = (255, 255, 255, 200)

img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# 圆角方块（垂直渐变：逐行画实心圆角矩形——用蒙版实现渐变）
mask = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, SIZE - 1, SIZE - 1], radius=RADIUS, fill=255)
grad = Image.new("RGBA", (SIZE, SIZE))
gd = ImageDraw.Draw(grad)
for y in range(SIZE):
    t = y / SIZE
    r = int(BLUE_TOP[0] + (BLUE_BOT[0] - BLUE_TOP[0]) * t)
    g = int(BLUE_TOP[1] + (BLUE_BOT[1] - BLUE_TOP[1]) * t)
    b = int(BLUE_TOP[2] + (BLUE_BOT[2] - BLUE_TOP[2]) * t)
    gd.line([(0, y), (SIZE, y)], fill=(r, g, b, 255))
img.paste(grad, (0, 0), mask)

# 截图选框四角（白色角括号，呼应"框选"）
L = 150          # 角括号臂长
T = 5            # 线宽
off = 90         # 距边缘
for cx, cy, sx, sy in [
    (off, off, 1, 1),                 # 左上
    (SIZE - off, off, -1, 1),         # 右上
    (off, SIZE - off, 1, -1),         # 左下
    (SIZE - off, SIZE - off, -1, -1), # 右下
]:
    draw.line([(cx, cy), (cx + sx * L, cy)], fill=FRAME, width=T)
    draw.line([(cx, cy), (cx, cy + sy * L)], fill=FRAME, width=T)

# 主字"拓"
font_path = r"C:\Windows\Fonts\msyhbd.ttc"
try:
    font = ImageFont.truetype(font_path, 430)
except OSError:
    font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 430)
bbox = draw.textbbox((0, 0), "映", font=font)
w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]
tx = (SIZE - w) / 2 - bbox[0]
ty = (SIZE - h) / 2 - bbox[1] - 20
# 阴影提升对比度
shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
ImageDraw.Draw(shadow).text((tx + 8, ty + 10), "映", font=font, fill=(0, 0, 30, 110))
img = Image.alpha_composite(img, shadow)
draw = ImageDraw.Draw(img)
draw.text((tx, ty), "映", font=font, fill=WHITE)

# 缩到 240 并保存多尺寸 ICO（全 BMP 帧——PIL 会把 256 帧存成 PNG 压缩，
# Win32 ExtractAssociatedIcon/部分场景读不了，导致图标显示为默认）
OUT = 240
main = img.resize((OUT, OUT), Image.LANCZOS)
out_path = Path(__file__).parent.parent / "src" / "Ta.App" / "ta.ico"
main.save(out_path, format="ICO",
          sizes=[(240, 240), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])
main.resize((128, 128), Image.LANCZOS).save(
    Path(__file__).parent.parent / "docs" / "icon-preview.png")
print("icon saved:", out_path)
