#pragma once

#include <Arduino.h>
#include <Display_ST7789.h>
#include <pgmspace.h>
#include <stdio.h>
#include <string.h>

constexpr uint16_t ASCII_SAFE_LEFT = 12;
constexpr uint16_t ASCII_SAFE_RIGHT = 12;
constexpr uint16_t ASCII_SAFE_TOP = 8;
constexpr uint16_t ASCII_SAFE_BOTTOM = 8;

// Printable ASCII characters 0x20 through 0x7E, five columns per glyph.
// Bit 0 is the top pixel. A blank sixth column and eighth row add spacing.
static const uint8_t ASCII_5X7[] PROGMEM = {
  0x00,0x00,0x00,0x00,0x00, // space
  0x00,0x00,0x5F,0x00,0x00, // !
  0x00,0x07,0x00,0x07,0x00, // "
  0x14,0x7F,0x14,0x7F,0x14, // #
  0x24,0x2A,0x7F,0x2A,0x12, // $
  0x23,0x13,0x08,0x64,0x62, // %
  0x36,0x49,0x55,0x22,0x50, // &
  0x00,0x05,0x03,0x00,0x00, // '
  0x00,0x1C,0x22,0x41,0x00, // (
  0x00,0x41,0x22,0x1C,0x00, // )
  0x14,0x08,0x3E,0x08,0x14, // *
  0x08,0x08,0x3E,0x08,0x08, // +
  0x00,0x50,0x30,0x00,0x00, // ,
  0x08,0x08,0x08,0x08,0x08, // -
  0x00,0x60,0x60,0x00,0x00, // .
  0x20,0x10,0x08,0x04,0x02, // /
  0x3E,0x51,0x49,0x45,0x3E, // 0
  0x00,0x42,0x7F,0x40,0x00, // 1
  0x42,0x61,0x51,0x49,0x46, // 2
  0x21,0x41,0x45,0x4B,0x31, // 3
  0x18,0x14,0x12,0x7F,0x10, // 4
  0x27,0x45,0x45,0x45,0x39, // 5
  0x3C,0x4A,0x49,0x49,0x30, // 6
  0x01,0x71,0x09,0x05,0x03, // 7
  0x36,0x49,0x49,0x49,0x36, // 8
  0x06,0x49,0x49,0x29,0x1E, // 9
  0x00,0x36,0x36,0x00,0x00, // :
  0x00,0x56,0x36,0x00,0x00, // ;
  0x08,0x14,0x22,0x41,0x00, // <
  0x14,0x14,0x14,0x14,0x14, // =
  0x00,0x41,0x22,0x14,0x08, // >
  0x02,0x01,0x51,0x09,0x06, // ?
  0x32,0x49,0x79,0x41,0x3E, // @
  0x7E,0x11,0x11,0x11,0x7E, // A
  0x7F,0x49,0x49,0x49,0x36, // B
  0x3E,0x41,0x41,0x41,0x22, // C
  0x7F,0x41,0x41,0x22,0x1C, // D
  0x7F,0x49,0x49,0x49,0x41, // E
  0x7F,0x09,0x09,0x09,0x01, // F
  0x3E,0x41,0x49,0x49,0x7A, // G
  0x7F,0x08,0x08,0x08,0x7F, // H
  0x00,0x41,0x7F,0x41,0x00, // I
  0x20,0x40,0x41,0x3F,0x01, // J
  0x7F,0x08,0x14,0x22,0x41, // K
  0x7F,0x40,0x40,0x40,0x40, // L
  0x7F,0x02,0x0C,0x02,0x7F, // M
  0x7F,0x04,0x08,0x10,0x7F, // N
  0x3E,0x41,0x41,0x41,0x3E, // O
  0x7F,0x09,0x09,0x09,0x06, // P
  0x3E,0x41,0x51,0x21,0x5E, // Q
  0x7F,0x09,0x19,0x29,0x46, // R
  0x46,0x49,0x49,0x49,0x31, // S
  0x01,0x01,0x7F,0x01,0x01, // T
  0x3F,0x40,0x40,0x40,0x3F, // U
  0x1F,0x20,0x40,0x20,0x1F, // V
  0x3F,0x40,0x38,0x40,0x3F, // W
  0x63,0x14,0x08,0x14,0x63, // X
  0x07,0x08,0x70,0x08,0x07, // Y
  0x61,0x51,0x49,0x45,0x43, // Z
  0x00,0x7F,0x41,0x41,0x00, // [
  0x02,0x04,0x08,0x10,0x20, // backslash
  0x00,0x41,0x41,0x7F,0x00, // ]
  0x04,0x02,0x01,0x02,0x04, // ^
  0x40,0x40,0x40,0x40,0x40, // _
  0x00,0x01,0x02,0x04,0x00, // `
  0x20,0x54,0x54,0x54,0x78, // a
  0x7F,0x48,0x44,0x44,0x38, // b
  0x38,0x44,0x44,0x44,0x20, // c
  0x38,0x44,0x44,0x48,0x7F, // d
  0x38,0x54,0x54,0x54,0x18, // e
  0x08,0x7E,0x09,0x01,0x02, // f
  0x0C,0x52,0x52,0x52,0x3E, // g
  0x7F,0x08,0x04,0x04,0x78, // h
  0x00,0x44,0x7D,0x40,0x00, // i
  0x20,0x40,0x44,0x3D,0x00, // j
  0x7F,0x10,0x28,0x44,0x00, // k
  0x00,0x41,0x7F,0x40,0x00, // l
  0x7C,0x04,0x18,0x04,0x78, // m
  0x7C,0x08,0x04,0x04,0x78, // n
  0x38,0x44,0x44,0x44,0x38, // o
  0x7C,0x14,0x14,0x14,0x08, // p
  0x08,0x14,0x14,0x18,0x7C, // q
  0x7C,0x08,0x04,0x04,0x08, // r
  0x48,0x54,0x54,0x54,0x20, // s
  0x04,0x3F,0x44,0x40,0x20, // t
  0x3C,0x40,0x40,0x20,0x7C, // u
  0x1C,0x20,0x40,0x20,0x1C, // v
  0x3C,0x40,0x30,0x40,0x3C, // w
  0x44,0x28,0x10,0x28,0x44, // x
  0x0C,0x50,0x50,0x50,0x3C, // y
  0x44,0x64,0x54,0x4C,0x44, // z
  0x00,0x08,0x36,0x41,0x00, // {
  0x00,0x00,0x7F,0x00,0x00, // |
  0x00,0x41,0x36,0x08,0x00, // }
  0x08,0x04,0x08,0x10,0x08  // ~
};

static inline uint16_t asciiSwap565(uint16_t color) {
  return (color >> 8) | (color << 8);
}

static inline void drawAsciiChar(uint16_t x, uint16_t y, char ch,
                                 uint8_t scale, uint16_t fg, uint16_t bg) {
  if (scale < 1) scale = 1;
  if (scale > 4) scale = 4;

  const uint16_t width = 6 * scale;
  const uint16_t height = 8 * scale;
  if (x >= LCD_WIDTH || y >= LCD_HEIGHT ||
      width > LCD_WIDTH - x || height > LCD_HEIGHT - y) return;

  if (ch < 0x20 || ch > 0x7E) ch = '?';
  const uint16_t fgSpi = asciiSwap565(fg);
  const uint16_t bgSpi = asciiSwap565(bg);
  static uint16_t pixels[6 * 8 * 4 * 4];

  for (uint16_t py = 0; py < height; ++py) {
    const uint8_t row = py / scale;
    for (uint16_t px = 0; px < width; ++px) {
      const uint8_t col = px / scale;
      const bool on = col < 5 && row < 7 &&
                      (pgm_read_byte(&ASCII_5X7[(ch - 0x20) * 5 + col]) & (1U << row));
      pixels[py * width + px] = on ? fgSpi : bgSpi;
    }
  }

  LCD_addWindow(x, y, x + width - 1, y + height - 1, pixels);
}

static inline void drawAsciiText(uint16_t x, uint16_t y, const char* text,
                                 uint8_t scale, uint16_t fg, uint16_t bg) {
  if (!text) return;
  if (scale < 1) scale = 1;
  if (scale > 4) scale = 4;

  const uint16_t startX = x;
  const uint16_t charWidth = 6 * scale;
  const uint16_t charHeight = 8 * scale;

  while (*text) {
    if (*text == '\n') {
      x = startX;
      y += charHeight;
      ++text;
      continue;
    }
    if (charWidth > LCD_WIDTH - x) {
      x = startX;
      y += charHeight;
    }
    if (y >= LCD_HEIGHT || charHeight > LCD_HEIGHT - y) return;
    drawAsciiChar(x, y, *text++, scale, fg, bg);
    x += charWidth;
  }
}

struct AsciiLcdConsole {
  uint16_t left;
  uint16_t top;
  uint16_t right;
  uint16_t bottom;
  uint16_t columns;
  uint16_t rows;
  uint16_t column;
  uint16_t row;
  uint8_t scale;
  uint16_t fg;
  uint16_t bg;
};

static AsciiLcdConsole asciiLcdConsole = {
  ASCII_SAFE_LEFT, ASCII_SAFE_TOP,
  LCD_WIDTH - ASCII_SAFE_RIGHT, LCD_HEIGHT - ASCII_SAFE_BOTTOM,
  1, 1, 0, 0, 1, 0xFFFF, 0x0000
};

static char asciiLcdCells[(LCD_WIDTH / 6) * (LCD_HEIGHT / 8)];
static FILE* asciiLcdStdout = nullptr;
static inline void lcdPrintfWrite(char ch);

static int asciiLcdWrite(void*, const char* data, int length) {
  for (int i = 0; i < length; ++i) lcdPrintfWrite(data[i]);
  return length;
}

static inline void asciiFillArea(uint16_t x, uint16_t y,
                                 uint16_t width, uint16_t height,
                                 uint16_t color) {
  constexpr uint16_t ROWS = 4;
  static uint16_t pixels[LCD_WIDTH * ROWS];
  const uint16_t spiColor = asciiSwap565(color);
  for (uint32_t i = 0; i < (uint32_t)width * ROWS; ++i) pixels[i] = spiColor;

  for (uint16_t row = 0; row < height; row += ROWS) {
    const uint16_t rows = (height - row > ROWS) ? ROWS : height - row;
    LCD_addWindow(x, y + row, x + width - 1, y + row + rows - 1, pixels);
  }
}

static inline void asciiRedrawConsole() {
  const AsciiLcdConsole& out = asciiLcdConsole;
  constexpr uint16_t BLOCK_ROWS = 4;
  static uint16_t pixels[LCD_WIDTH * BLOCK_ROWS];
  const uint16_t width = out.right - out.left;
  const uint16_t charWidth = 6 * out.scale;
  const uint16_t charHeight = 8 * out.scale;
  const uint16_t fg = asciiSwap565(out.fg);
  const uint16_t bg = asciiSwap565(out.bg);

  for (uint16_t textRow = 0; textRow < out.rows; ++textRow) {
    for (uint16_t block = 0; block < charHeight; block += BLOCK_ROWS) {
      const uint16_t blockRows =
          (charHeight - block > BLOCK_ROWS) ? BLOCK_ROWS : charHeight - block;

      for (uint16_t py = 0; py < blockRows; ++py) {
        const uint8_t glyphRow = (block + py) / out.scale;
        for (uint16_t px = 0; px < width; ++px) {
          bool on = false;
          const uint16_t textColumn = px / charWidth;
          if (textColumn < out.columns) {
            char ch = asciiLcdCells[textRow * out.columns + textColumn];
            if (ch < 0x20 || ch > 0x7E) ch = '?';
            const uint8_t glyphColumn = (px % charWidth) / out.scale;
            on = glyphColumn < 5 && glyphRow < 7 &&
                 (pgm_read_byte(&ASCII_5X7[(ch - 0x20) * 5 + glyphColumn]) &
                  (1U << glyphRow));
          }
          pixels[py * width + px] = on ? fg : bg;
        }
      }

      const uint16_t y = out.top + textRow * charHeight + block;
      LCD_addWindow(out.left, y, out.right - 1, y + blockRows - 1, pixels);
    }
  }
}

static inline void lcdPrintfNewLine() {
  AsciiLcdConsole& out = asciiLcdConsole;
  out.column = 0;
  if (++out.row < out.rows) {
    return;
  }

  const size_t rowBytes = out.columns;
  memmove(asciiLcdCells, asciiLcdCells + rowBytes,
          rowBytes * (out.rows - 1));
  memset(asciiLcdCells + rowBytes * (out.rows - 1), ' ', rowBytes);
  out.row = out.rows - 1;
  asciiRedrawConsole();
}

static inline void lcdPrintfBegin(uint16_t x, uint16_t y, uint8_t scale,
                                  uint16_t fg, uint16_t bg) {
  if (scale < 1) scale = 1;
  if (scale > 4) scale = 4;
  const uint16_t charWidth = 6 * scale;
  const uint16_t charHeight = 8 * scale;
  if (x < ASCII_SAFE_LEFT) x = ASCII_SAFE_LEFT;
  if (y < ASCII_SAFE_TOP) y = ASCII_SAFE_TOP;
  const uint16_t right = LCD_WIDTH - ASCII_SAFE_RIGHT;
  const uint16_t bottom = LCD_HEIGHT - ASCII_SAFE_BOTTOM;
  if (x + charWidth > right) x = ASCII_SAFE_LEFT;
  if (y + charHeight > bottom) y = ASCII_SAFE_TOP;

  const uint16_t columns = (right - x) / charWidth;
  const uint16_t rows = (bottom - y) / charHeight;
  asciiLcdConsole = {x, y, right, bottom, columns, rows,
                     0, 0, scale, fg, bg};
  memset(asciiLcdCells, ' ', columns * rows);
  asciiFillArea(x, y, right - x, bottom - y, bg);

  if (!asciiLcdStdout) {
    asciiLcdStdout = funopen(nullptr, nullptr, asciiLcdWrite, nullptr, nullptr);
    if (asciiLcdStdout) setvbuf(asciiLcdStdout, nullptr, _IONBF, 0);
  }
  if (asciiLcdStdout) stdout = asciiLcdStdout;
}

static inline void lcdPrintfResetScroll() {
  AsciiLcdConsole& out = asciiLcdConsole;
  memset(asciiLcdCells, ' ', out.columns * out.rows);
  out.column = 0;
  out.row = 0;
  asciiFillArea(out.left, out.top,
                out.right - out.left, out.bottom - out.top, out.bg);
}

static inline void lcdPrintfWrite(char ch) {
  AsciiLcdConsole& out = asciiLcdConsole;
  const uint16_t charWidth = 6 * out.scale;
  const uint16_t charHeight = 8 * out.scale;

  if (ch == '\r') return;
  if (ch == '\n') {
    lcdPrintfNewLine();
    return;
  }
  if (out.column >= out.columns) {
    lcdPrintfNewLine();
  }

  asciiLcdCells[out.row * out.columns + out.column] = ch;
  drawAsciiChar(out.left + out.column * charWidth,
                out.top + out.row * charHeight,
                ch, out.scale, out.fg, out.bg);
  ++out.column;
}
