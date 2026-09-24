#include <stdint.h>
#include "bsp_cst816.h"
#include "Wire.h"
TwoWire *g_touch_i2c;
static uint16_t g_width;
static uint16_t g_height;

static bool g_touch_flag;
static uint8_t g_touch_rotation = 1;
static uint16_t g_touchpad_x;
static uint16_t g_touchpad_y;

bool bsp_touch_i2c_reg8_read(uint8_t dev_addr, uint8_t reg_addr, uint8_t *data, uint8_t len) {
  uint8_t error;
  g_touch_i2c->beginTransmission(dev_addr);
  g_touch_i2c->write(reg_addr);
  error = g_touch_i2c->endTransmission(true);
  if (error) {
    Serial.printf("endTransmission: %u\n", error);
    return false;
  }
  g_touch_i2c->requestFrom(dev_addr, len);
  for (int i = 0; i < len; i++) {
    *data++ = g_touch_i2c->read();
  }
  return true;

  return false;
}

bool bsp_touch_i2c_reg8_write(uint8_t dev_addr, uint8_t reg_addr, uint8_t *data, uint8_t len) {
  uint8_t error;
  g_touch_i2c->beginTransmission(dev_addr);
  g_touch_i2c->write(reg_addr);
  for (int i = 0; i < len; i++) {
    g_touch_i2c->write(*data++);
  }
  error = g_touch_i2c->endTransmission(true);
  if (error) {
    Serial.printf("endTransmission: %u\n", error);
    return false;
  }
  return true;

  return false;
}


bool bsp_touch_init(TwoWire *touch_i2c, uint8_t touch_rotation, uint16_t width, uint16_t height) {
  uint8_t id;
  g_touch_rotation = touch_rotation;
  g_width = width;
  g_height = height;
  g_touch_i2c = touch_i2c;
  if (EXAMPLE_PIN_NUM_TP_RST != -1) {
    pinMode(EXAMPLE_PIN_NUM_TP_RST, OUTPUT);
    // 复位
    digitalWrite(EXAMPLE_PIN_NUM_TP_RST, LOW);
    delay(200);
    digitalWrite(EXAMPLE_PIN_NUM_TP_RST, HIGH);
    delay(300);
  }
  bsp_touch_i2c_reg8_read(CST816_ADDR, CST816_ID_REG, &id, 1);
  if (0xB6 == id) {
    Serial.println("Find CST816!");
    return true;
  }
  Serial.println("QMI8658 read data fail!");
  return false;
}

void bsp_touch_read(void) {
  uint8_t touch_num = 0;

  uint8_t touch_raw[2];
  bsp_touch_i2c_reg8_read(CST816_ADDR, CST816_TOUCH_NUM_REG, &touch_num, 1);
  if (touch_num == 0)
    return;

  bsp_touch_i2c_reg8_read(CST816_ADDR, CST816_TOUCH_XH_REG, &touch_raw[0], 1);
  bsp_touch_i2c_reg8_read(CST816_ADDR, CST816_TOUCH_XL_REG, &touch_raw[1], 1);
  g_touchpad_x = (uint16_t)((touch_raw[0] & 0x0f) << 8);
  g_touchpad_x |= touch_raw[1];

  touch_raw[0] = 0;
  touch_raw[1] = 0;
  bsp_touch_i2c_reg8_read(CST816_ADDR, CST816_TOUCH_YH_REG, &touch_raw[0], 1);
  bsp_touch_i2c_reg8_read(CST816_ADDR, CST816_TOUCH_YL_REG, &touch_raw[1], 1);
  g_touchpad_y = (uint16_t)((touch_raw[0] & 0x0f) << 8);
  g_touchpad_y |= touch_raw[1];
  g_touch_flag = true;
}

bool bsp_touch_get_coordinates(uint16_t *touchpad_x, uint16_t *touchpad_y) {
  if (g_touch_flag == false)
    return false;
  g_touch_flag = false;
  switch (g_touch_rotation) {
    case 1:
      *touchpad_x = g_touchpad_y;
      *touchpad_y = g_height - 1 - g_touchpad_x;
      break;
    case 2:
      *touchpad_x = g_width - 1 - g_touchpad_x;
      *touchpad_y = g_height - 1 - g_touchpad_y;
      break;
    case 3:
      *touchpad_x = g_width - 1 - g_touchpad_y;
      *touchpad_y = g_touchpad_x;
      break;
    default:
      *touchpad_x = g_touchpad_x;
      *touchpad_y = g_touchpad_y;
      break;
  }
  return true;
}
