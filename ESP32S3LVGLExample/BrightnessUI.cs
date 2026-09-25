using System;
using System.Runtime;
using System.Runtime.InteropServices;
using static LVGL;

internal static unsafe partial class BrightnessUI
{
    [RuntimeExport("lvgl_brightness_ui_init")]
    private static void Initialize(LVObject brightnessScreen)
    {
        LVObject aboutScreen = CreateScreen();
        LVObject widgetsScreen = CreateScreen();
        BuildBrightnessScreen(brightnessScreen, aboutScreen);
        BuildAboutScreen(aboutScreen, widgetsScreen);
        BuildWidgetsScreen(widgetsScreen, brightnessScreen);
    }

    [UnmanagedCallersOnly]
    private static void SwitchScreen(LVEvent evt)
    {
        if (evt.Code == LV_EVENT_CLICKED)
            LoadScreen(new LVObject(evt.UserData));
    }

    [UnmanagedCallersOnly]
    private static void BrightnessSliderChanged(LVEvent evt)
    {
        if (evt.Code != LV_EVENT_VALUE_CHANGED && evt.Code != LV_EVENT_PRESSING)
            return;

        LVSlider slider = new LVSlider(evt.Target);
        LVLabel valueLabel = new LVLabel(new LVObject(evt.UserData));
        int value = slider.GetValue();
        brightnessBar.SetValue(value);
        SetBrightnessText(valueLabel, value);
        SetLcdBrightness(value);
        evt.StopBubbling();
    }

    [UnmanagedCallersOnly]
    private static void ResetBrightness(LVEvent evt)
    {
        if (evt.Code != LV_EVENT_CLICKED)
            return;

        brightnessSlider.SetValue(80);
        UpdateBrightness(80);
        evt.StopBubbling();
    }

    private static void UpdateBrightness(int value)
    {
        brightnessBar.SetValue(value);
        SetBrightnessText(brightnessValue, value);
        SetLcdBrightness(value);
    }

    private static void SetBrightnessText(LVLabel label, int value)
    {
        byte* text = stackalloc byte[5];
        int index = 0;

        if (value >= 100)
            text[index++] = (byte)('0' + value / 100);
        if (value >= 10)
            text[index++] = (byte)('0' + value / 10 % 10);
        text[index++] = (byte)('0' + value % 10);
        text[index++] = (byte)'%';
        text[index] = 0;
        label.SetText(text);
    }

    [UnmanagedCallersOnly]
    private static void TemperatureChanged(LVEvent evt)
    {
        int value = new LVArc(evt.Target).Value;
        byte* text = stackalloc byte[8];
        int index = 0;
        if (value >= 100)
            text[index++] = (byte)('0' + value / 100);
        if (value >= 10)
            text[index++] = (byte)('0' + value / 10 % 10);
        text[index++] = (byte)('0' + value % 10);
        text[index++] = (byte)' ';
        text[index++] = 0xC2;
        text[index++] = 0xB0;
        text[index++] = (byte)'C';
        text[index] = 0;
        temperatureLabel.SetText(text);
    }

    [UnmanagedCallersOnly]
    private static void GallerySliderChanged(LVEvent evt)
    {
        galleryBar.SetValue(new LVSlider(evt.Target).GetValue());
    }

    [DllImport("*", EntryPoint = "managed_set_lcd_brightness")]
    private static extern void SetLcdBrightness(int percent);
}
