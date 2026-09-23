#pragma once
#include <Arduino.h>
#include <SPI.h>
#define VERTICAL 0 // 0: landscape (320x240); 1: portrait (240x320)
#if VERTICAL
#define EXAMPLE_LCD_H_RES 240
#define EXAMPLE_LCD_V_RES 320
#define LCD_MADCTL 0x00
#else
#define EXAMPLE_LCD_H_RES 320
#define EXAMPLE_LCD_V_RES 240
#define LCD_MADCTL 0x60
#endif
#define LCD_WIDTH  EXAMPLE_LCD_H_RES
#define LCD_HEIGHT EXAMPLE_LCD_V_RES

#define SPIFreq 40000000
#define EXAMPLE_PIN_NUM_LCD_SCLK 39
#define EXAMPLE_PIN_NUM_LCD_MOSI 38
#define EXAMPLE_PIN_NUM_LCD_MISO 40
#define EXAMPLE_PIN_NUM_LCD_DC   42
#define EXAMPLE_PIN_NUM_LCD_RST  -1
#define EXAMPLE_PIN_NUM_LCD_CS   45
#define EXAMPLE_PIN_NUM_LCD_BL   1
#define Frequency       1000
#define Resolution      10
#define Backlight_MAX   100

#define Offset_X 0
#define Offset_Y 0

extern uint8_t LCD_Backlight;

void LCD_SetCursor(uint16_t x1, uint16_t y1, uint16_t x2,uint16_t y2);

void LCD_Init(void);
void LCD_SetCursor(uint16_t Xstart, uint16_t Ystart, uint16_t Xend, uint16_t  Yend);
void LCD_addWindow(uint16_t Xstart, uint16_t Ystart, uint16_t Xend, uint16_t Yend,uint16_t* color);

void Backlight_Init(void);
void Set_Backlight(uint8_t Light);
