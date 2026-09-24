using System;
using System.Runtime;
using System.Runtime.InteropServices;
using static LVGL;

internal static unsafe class BrightnessUI
{
    private static LVSlider brightnessSlider;
    private static LVBar brightnessBar;
    private static LVLabel brightnessValue;

    [RuntimeExport("lvgl_brightness_ui_init")]
    private static void Initialize(IntPtr parentHandle)
    {
        LVObject screen = new LVObject(parentHandle);

        LVObject panel = CreateObject(screen);
        panel.SetSizePercent(90, 90);
        panel.Center();
        panel.SetStylePadAll(0);
        panel.SetStylePadTop(24);
        panel.SetStylePadBottom(24);
        panel.ClearFlag(LV_OBJ_FLAG_GESTURE_BUBBLE);
        panel.AddFlag(LV_OBJ_FLAG_SCROLLABLE);
        panel.UpdateLayout();

        LVLabel title = CreateLabel(panel);
        title.SetText("LCD brightness"u8);
        title.Align(LVAlign.TopMid);

        brightnessValue = CreateLabel(panel);
        SetBrightnessText(brightnessValue, 80);
        brightnessValue.Object.AlignTo(title.Object, LVAlign.OutBottomMid, 0, 12);

        brightnessSlider = CreateSlider(panel);
        brightnessSlider.SetRange(0, 100);
        brightnessSlider.SetValue(80);
        brightnessSlider.Object.SetWidthPercent(80);
        brightnessSlider.Object.SetHeight(24);
        brightnessSlider.Object.AlignTo(brightnessValue.Object, LVAlign.OutBottomMid, 0, 24);
        brightnessSlider.AddEventCallback(&BrightnessSliderChanged, LV_EVENT_ALL, brightnessValue.Handle);

        brightnessBar = CreateBar(panel);
        brightnessBar.SetRange(0, 100);
        brightnessBar.SetValue(80);
        brightnessBar.Object.SetWidthPercent(80);
        brightnessBar.Object.SetHeight(14);
        brightnessBar.Object.AlignTo(brightnessSlider.Object, LVAlign.OutBottomMid, 0, 16);

        LVSwitch autoSwitch = CreateSwitch(panel);
        autoSwitch.Object.AlignTo(brightnessBar.Object, LVAlign.OutBottomLeft, 14, 24);

        LVLabel autoLabel = CreateLabel(panel);
        autoLabel.SetText("Auto brightness"u8);
        autoLabel.Object.AlignTo(autoSwitch.Object, LVAlign.OutRightMid, 8, 0);

        LVCheckbox displayCheckbox = CreateCheckbox(panel);
        displayCheckbox.SetText("Display enabled"u8);
        displayCheckbox.SetChecked(true);
        displayCheckbox.Object.AlignTo(autoSwitch.Object, LVAlign.OutBottomLeft, -6, 24);

        LVButton resetButton = CreateButton(panel);
        resetButton.SetSize(82, 34);
        resetButton.Object.AlignTo(displayCheckbox.Object, LVAlign.OutBottomRight, 0, 16);
        LVLabel resetLabel = resetButton.CreateLabel();
        resetLabel.SetText("Reset"u8);
        resetLabel.Align(LVAlign.Center);
        resetButton.AddEventCallback(&ResetBrightness, LV_EVENT_CLICKED);
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
