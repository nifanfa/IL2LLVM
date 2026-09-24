#include <Arduino.h>
#include <Arduino_GFX_Library.h>
#include <lvgl.h>
#include <Wire.h>
#include <esp_heap_caps.h>

#include "bsp_cst816.h"

#define EXAMPLE_PIN_NUM_LCD_SCLK 39
#define EXAMPLE_PIN_NUM_LCD_MOSI 38
#define EXAMPLE_PIN_NUM_LCD_MISO 40
#define EXAMPLE_PIN_NUM_LCD_DC 42
#define EXAMPLE_PIN_NUM_LCD_RST -1
#define EXAMPLE_PIN_NUM_LCD_CS 45
#define EXAMPLE_PIN_NUM_LCD_BL 1
#define EXAMPLE_PIN_NUM_TP_SDA 48
#define EXAMPLE_PIN_NUM_TP_SCL 47

#define LEDC_FREQ 5000
#define LEDC_TIMER_10_BIT 10
#define EXAMPLE_LCD_ROTATION 1
#define EXAMPLE_LCD_H_RES 240
#define EXAMPLE_LCD_V_RES 320

extern "C" void lvgl_brightness_ui_init(lv_obj_t *parent);

Arduino_DataBus *bus = new Arduino_ESP32SPI(
    EXAMPLE_PIN_NUM_LCD_DC,
    EXAMPLE_PIN_NUM_LCD_CS,
    EXAMPLE_PIN_NUM_LCD_SCLK,
    EXAMPLE_PIN_NUM_LCD_MOSI,
    EXAMPLE_PIN_NUM_LCD_MISO);

Arduino_GFX *gfx = new Arduino_ST7789(
    bus,
    EXAMPLE_PIN_NUM_LCD_RST,
    EXAMPLE_LCD_ROTATION,
    true,
    EXAMPLE_LCD_H_RES,
    EXAMPLE_LCD_V_RES);

uint32_t screenWidth;
uint32_t screenHeight;
uint32_t bufSize;
lv_disp_draw_buf_t draw_buf;
lv_color_t *disp_draw_buf;
lv_disp_drv_t disp_drv;

extern "C" void managed_set_lcd_brightness(int percent)
{
    if (percent < 0)
        percent = 0;
    if (percent > 100)
        percent = 100;

    ledcWrite(EXAMPLE_PIN_NUM_LCD_BL, (1 << LEDC_TIMER_10_BIT) / 100 * percent);
}

static void my_disp_flush(lv_disp_drv_t *displayDriver, const lv_area_t *area, lv_color_t *color)
{
    lv_disp_flush_ready(displayDriver);
}

static void my_touchpad_read(lv_indev_drv_t *inputDriver, lv_indev_data_t *data)
{
    uint16_t touchX;
    uint16_t touchY;

    bsp_touch_read();
    if (bsp_touch_get_coordinates(&touchX, &touchY))
    {
        data->point.x = touchX;
        data->point.y = touchY;
        data->state = LV_INDEV_STATE_PR;
    }
    else
    {
        data->state = LV_INDEV_STATE_REL;
    }
}

void setup()
{
    Serial.begin(115200);

    if (!gfx->begin())
        Serial.println("gfx->begin() failed!");
    gfx->fillScreen(BLACK);

    ledcAttach(EXAMPLE_PIN_NUM_LCD_BL, LEDC_FREQ, LEDC_TIMER_10_BIT);
    managed_set_lcd_brightness(80);

    Wire.begin(EXAMPLE_PIN_NUM_TP_SDA, EXAMPLE_PIN_NUM_TP_SCL);
    bsp_touch_init(&Wire, gfx->getRotation(), gfx->width(), gfx->height());

    lv_init();

    screenWidth = gfx->width();
    screenHeight = gfx->height();
    bufSize = screenWidth * screenHeight;

    disp_draw_buf = (lv_color_t *)heap_caps_malloc(
        bufSize * 2,
        MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
    if (disp_draw_buf == nullptr)
        disp_draw_buf = (lv_color_t *)heap_caps_malloc(bufSize * 2, MALLOC_CAP_8BIT);

    if (disp_draw_buf == nullptr)
    {
        Serial.println("LVGL disp_draw_buf allocate failed!");
        return;
    }

    lv_disp_draw_buf_init(&draw_buf, disp_draw_buf, nullptr, bufSize);

    lv_disp_drv_init(&disp_drv);
    disp_drv.hor_res = screenWidth;
    disp_drv.ver_res = screenHeight;
    disp_drv.flush_cb = my_disp_flush;
    disp_drv.draw_buf = &draw_buf;
    disp_drv.direct_mode = true;
    lv_disp_drv_register(&disp_drv);

    static lv_indev_drv_t indev_drv;
    lv_indev_drv_init(&indev_drv);
    indev_drv.type = LV_INDEV_TYPE_POINTER;
    indev_drv.read_cb = my_touchpad_read;
    lv_indev_drv_register(&indev_drv);

    lvgl_brightness_ui_init(lv_scr_act());
    Serial.println("Setup done");
}

void loop()
{
    lv_timer_handler();

#if (LV_COLOR_16_SWAP != 0)
    gfx->draw16bitBeRGBBitmap(
        0,
        0,
        (uint16_t *)disp_draw_buf,
        screenWidth,
        screenHeight);
#else
    gfx->draw16bitRGBBitmap(
        0,
        0,
        (uint16_t *)disp_draw_buf,
        screenWidth,
        screenHeight);
#endif

    delay(5);
}
