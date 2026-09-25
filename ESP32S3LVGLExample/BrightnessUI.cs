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
        BuildBrightnessScreen(brightnessScreen, aboutScreen);
        BuildAboutScreen(aboutScreen, brightnessScreen);
    }

    [UnmanagedCallersOnly]
    private static void SwitchScreen(IntPtr eventHandle)
    {
        LVEvent evt = new LVEvent(eventHandle);
        if (evt.Code == LV_EVENT_CLICKED)
            LoadScreen(new LVObject(evt.UserData));
    }

    [UnmanagedCallersOnly]
    private static void BrightnessSliderChanged(IntPtr eventHandle)
    {
        LVEvent evt = new LVEvent(eventHandle);
        if (evt.Code != LV_EVENT_VALUE_CHANGED && evt.Code != LV_EVENT_PRESSING)
            return;

        LVSlider slider = new LVSlider(evt.Target.Handle);
        LVLabel valueLabel = new LVLabel(evt.UserData);
        int value = slider.GetValue();
        brightnessBar.SetValue(value);
        SetBrightnessText(valueLabel, value);
        SetLcdBrightness(value);
        evt.StopBubbling();
    }

    [UnmanagedCallersOnly]
    private static void ResetBrightness(IntPtr eventHandle)
    {
        LVEvent evt = new LVEvent(eventHandle);
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

    [DllImport("*", EntryPoint = "managed_set_lcd_brightness")]
    private static extern void SetLcdBrightness(int percent);
}
