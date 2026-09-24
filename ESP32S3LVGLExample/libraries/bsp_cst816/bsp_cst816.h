#pragma once
#include <Arduino.h>
#include <stdio.h>
#include <string.h>
#include <stdbool.h>
#include "Wire.h"

#define CST816_ADDR             (0x15)
#define CST816_ID_REG           (0xA7)
#define CST816_TOUCH_NUM_REG    (0X02)
#define CST816_TOUCH_XH_REG     (0x03)
#define CST816_TOUCH_XL_REG     (0x04)
#define CST816_TOUCH_YH_REG     (0x05)
#define CST816_TOUCH_YL_REG     (0x06)

#define EXAMPLE_PIN_NUM_TP_RST    (-1) 

bool bsp_touch_init(TwoWire *touch_i2c, uint8_t touch_rotation, uint16_t width, uint16_t height);
void bsp_touch_read(void);
bool bsp_touch_get_coordinates(uint16_t *touchpad_x, uint16_t *touchpad_y);
