#include <stdio.h>

#include "Display_ST7789.h"
#include "AsciiFont5x7.h"

extern "C" void managed_Main(void);

void LCD_WriteData_Word(uint16_t Data);

uint16_t RGB(uint8_t r, uint8_t g, uint8_t b) {
    uint16_t color = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
    return color;
}

void drawPixelRGB(uint16_t x, uint16_t y,
                  uint8_t r, uint8_t g, uint8_t b)
{
    LCD_SetCursor(x, y, x, y);
    LCD_WriteData_Word(RGB(r, g, b));
}

void fillRectRGB(uint16_t x, uint16_t y, uint16_t w, uint16_t h,
                 uint8_t r, uint8_t g, uint8_t b) {
  constexpr uint16_t N = 4;
  static uint16_t buf[LCD_WIDTH * N];

  if (!w || !h || x >= LCD_WIDTH || y >= LCD_HEIGHT) return;
  if (w > LCD_WIDTH - x)  w = LCD_WIDTH - x;
  if (h > LCD_HEIGHT - y) h = LCD_HEIGHT - y;

  uint16_t c = RGB(r, g, b);
  c = (c >> 8) | (c << 8);

  for (uint16_t i = 0; i < w * N; i++) buf[i] = c;

  for (uint16_t row = 0; row < h; row += N) {
    uint16_t rows = (h - row > N) ? N : h - row;
    LCD_addWindow(x, y + row, x + w - 1, y + row + rows - 1, buf);
  }
}

void setup() {
  LCD_Init();
  Set_Backlight(10);
  fillRectRGB(0,0,LCD_WIDTH,LCD_HEIGHT,0,0,0);
  lcdPrintfBegin(ASCII_SAFE_LEFT, ASCII_SAFE_TOP, 1,
                 RGB(255,255,255), RGB(0,0,0));
  managed_Main();
}

void loop() {
  delay(1);
}
