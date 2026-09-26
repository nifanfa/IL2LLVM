using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
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
        fixed (byte* ptr = Encoding.UTF8.GetBytes($"{value}%"))
            label.SetText(ptr);
    }

    [UnmanagedCallersOnly]
    private static void TemperatureChanged(LVEvent evt)
    {
        int value = new LVArc(evt.Target).Value;
        fixed (byte* ptr = Encoding.UTF8.GetBytes($"{value} °C"))
            temperatureLabel.SetText(ptr);
    }

    [UnmanagedCallersOnly]
    private static void GallerySliderChanged(LVEvent evt)
    {
        galleryBar.SetValue(new LVSlider(evt.Target).GetValue());
    }

    [DllImport("*", EntryPoint = "managed_set_lcd_brightness")]
    private static extern void SetLcdBrightness(int percent);
}
