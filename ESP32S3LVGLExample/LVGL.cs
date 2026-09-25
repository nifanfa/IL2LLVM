using System;
using System.Runtime.InteropServices;
using static LVGL;

public enum LVAlign
{
    Default = 0, TopLeft = 1, TopMid = 2, TopRight = 3,
    BottomLeft = 4, BottomMid = 5, BottomRight = 6,
    LeftMid = 7, RightMid = 8, Center = 9,
    OutTopLeft = 10, OutTopMid = 11, OutTopRight = 12,
    OutBottomLeft = 13, OutBottomMid = 14, OutBottomRight = 15,
    OutLeftTop = 16, OutLeftMid = 17, OutLeftBottom = 18,
    OutRightTop = 19, OutRightMid = 20, OutRightBottom = 21,
}

public readonly unsafe struct LVObject
{
    public readonly IntPtr Handle;
    public LVObject(IntPtr handle) => Handle = handle;
    public bool IsValid => Handle != IntPtr.Zero;
    public int X => LVGL.GetX(this);
    public int Y => LVGL.GetY(this);
    public int Width => LVGL.GetWidth(this);
    public int Height => LVGL.GetHeight(this);
    public LVObject Parent => LVGL.GetParent(this);
    public void SetPosition(int x, int y) => LVGL.SetPosition(this, x, y);
    public void SetX(int value) => LVGL.SetX(this, value);
    public void SetY(int value) => LVGL.SetY(this, value);
    public void SetSize(int width, int height) => LVGL.SetSize(this, width, height);
    public void SetWidth(int value) => LVGL.SetWidth(this, value);
    public void SetHeight(int value) => LVGL.SetHeight(this, value);
    public void SetSizePercent(int width, int height) => LVGL.SetSize(this, LVGL.Percentage(width), LVGL.Percentage(height));
    public void SetWidthPercent(int value) => LVGL.SetWidth(this, LVGL.Percentage(value));
    public void SetHeightPercent(int value) => LVGL.SetHeight(this, LVGL.Percentage(value));
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => LVGL.Align(this, (int)align, xOffset, yOffset);
    public void AlignTo(LVObject baseObject, LVAlign align, int xOffset = 0, int yOffset = 0) => LVGL.AlignTo(this, baseObject, (int)align, xOffset, yOffset);
    public void Center() => Align(LVAlign.Center);
    public void UpdateLayout() => LVGL.UpdateLayout(this);
    public void SetStylePadLeft(int value, uint selector = 0) => LVGL.SetStylePadLeft(this, value, selector);
    public void SetStylePadRight(int value, uint selector = 0) => LVGL.SetStylePadRight(this, value, selector);
    public void SetStylePadTop(int value, uint selector = 0) => LVGL.SetStylePadTop(this, value, selector);
    public void SetStylePadBottom(int value, uint selector = 0) => LVGL.SetStylePadBottom(this, value, selector);
    public void SetStylePadAll(int value, uint selector = 0) => LVGL.SetStylePadAll(this, value, selector);
    public void SetStyleArcOpacity(byte value, uint selector = 0) => LVGL.SetStyleArcOpacity(this, value, selector);
    public void SetStyleArcColor(uint color, uint selector = 0) => LVGL.SetStyleArcColor(this, color, selector);
    public void SetStyleBackgroundColor(uint color, uint selector = 0) => LVGL.SetStyleBackgroundColor(this, color, selector);
    public void SetStyleShadowWidth(int value, uint selector = 0) => LVGL.SetStyleShadowWidth(this, value, selector);
    public void SetStyleShadowOpacity(byte value, uint selector = 0) => LVGL.SetStyleShadowOpacity(this, value, selector);
    public void SetStyleShadowOffsetY(int value, uint selector = 0) => LVGL.SetStyleShadowOffsetY(this, value, selector);
    public void SetStyleThemeFontLarge(uint selector = 0) => LVGL.SetStyleThemeFontLarge(this, selector);
    public void ClearFlag(uint flag) => LVGL.ClearFlag(this, flag);
    public void AddFlag(uint flag) => LVGL.AddFlag(this, flag);
    public bool HasFlag(uint flag) => LVGL.HasFlag(this, flag);
    public void AddState(uint state) => LVGL.AddState(this, state);
    public void ClearState(uint state) => LVGL.ClearState(this, state);
    public bool HasState(uint state) => LVGL.HasState(this, state);
    public void SetExtClickArea(int value) => LVGL.SetExtClickArea(this, value);
    public void ScrollTo(int x, int y, bool animate = false) => LVGL.ScrollTo(this, x, y, animate ? 1 : 0);
    public void ScrollBy(int x, int y, bool animate = false) => LVGL.ScrollBy(this, x, y, animate ? 1 : 0);
    public void SetScrollDirection(byte direction) => LVGL.SetScrollDirection(this, direction);
    public void Invalidate() => LVGL.Invalidate(this);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => LVGL.AddEventCallback(this, callback, filter, userData);
}

public readonly unsafe struct LVLabel
{
    public readonly LVObject Object;
    public LVLabel(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetSize(int width, int height) => Object.SetSize(width, height);
    public void SetSizePercent(int width, int height) => Object.SetSizePercent(width, height);
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => Object.Align(align, xOffset, yOffset);
    public void SetLongMode(byte mode) => LVGL.SetLabelLongMode(this, mode);
    public void SetRecolor(bool enabled) => LVGL.SetLabelRecolor(this, (byte)(enabled ? 1 : 0));
    public void SetText(byte* text) => LVGL.SetLabelText(this, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetLabelText(this, text);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVButton
{
    public readonly LVObject Object;
    public LVButton(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetSize(int width, int height) => Object.SetSize(width, height);
    public void SetSizePercent(int width, int height) => Object.SetSizePercent(width, height);
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => Object.Align(align, xOffset, yOffset);
    public LVLabel CreateLabel() => LVGL.CreateLabel(Object);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVCheckbox
{
    public readonly LVObject Object;
    public LVCheckbox(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetText(byte* text) => LVGL.SetCheckboxText(this, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetCheckboxText(this, text);
    public void SetChecked(bool value) => LVGL.SetChecked(Object, (byte)(value ? 1 : 0));
    public bool IsChecked => LVGL.HasState(Object, LV_STATE_CHECKED);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVSwitch
{
    public readonly LVObject Object;
    public LVSwitch(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetChecked(bool value) => LVGL.SetChecked(Object, (byte)(value ? 1 : 0));
    public bool IsChecked => LVGL.HasState(Object, LV_STATE_CHECKED);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVBar
{
    public readonly LVObject Object;
    public LVBar(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetRange(int minimum, int maximum) => LVGL.SetBarRange(Object, minimum, maximum);
    public void SetValue(int value, bool animate = false) => LVGL.SetBarValue(Object, value, animate ? 1 : 0);
    public void SetStartValue(int value, bool animate = false) => LVGL.SetBarStartValue(Object, value, animate ? 1 : 0);
    public int Value => LVGL.GetBarValue(Object);
    public int StartValue => LVGL.GetBarStartValue(Object);
    public int Minimum => LVGL.GetBarMinimum(Object);
    public int Maximum => LVGL.GetBarMaximum(Object);
    public void SetMode(byte mode) => LVGL.SetBarMode(Object, mode);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVSlider
{
    public readonly LVObject Object;
    public LVSlider(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetSize(int width, int height) => Object.SetSize(width, height);
    public void SetSizePercent(int width, int height) => Object.SetSizePercent(width, height);
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => Object.Align(align, xOffset, yOffset);
    public void SetRange(int minimum, int maximum) => LVGL.SetBarRange(Object, minimum, maximum);
    public void SetValue(int value, bool animate = false) => LVGL.SetBarValue(Object, value, animate ? 1 : 0);
    public void SetLeftValue(int value, bool animate = false) => LVGL.SetBarStartValue(Object, value, animate ? 1 : 0);
    public int GetValue() => LVGL.GetBarValue(Object);
    public int GetLeftValue() => LVGL.GetBarStartValue(Object);
    public bool IsDragged => LVGL.IsSliderDragged(this);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVArc
{
    public readonly LVObject Object;
    public LVArc(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetAngles(ushort start, ushort end) => LVGL.SetArcAngles(this, start, end);
    public void SetBackgroundAngles(ushort start, ushort end) => LVGL.SetArcBackgroundAngles(this, start, end);
    public void SetRotation(ushort rotation) => LVGL.SetArcRotation(this, rotation);
    public void SetRange(short minimum, short maximum) => LVGL.SetArcRange(this, minimum, maximum);
    public void SetValue(short value) => LVGL.SetArcValue(this, value);
    public short Value => LVGL.GetArcValue(this);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVDropdown
{
    public readonly LVObject Object;
    public LVDropdown(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetOptions(byte* options) => LVGL.SetDropdownOptions(this, options);
    public void SetOptions(ReadOnlySpan<byte> options) => LVGL.SetDropdownOptions(this, options);
    public void SetText(byte* text) => LVGL.SetDropdownText(this, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetDropdownText(this, text);
    public void SetSelected(ushort index) => LVGL.SetDropdownSelected(this, index);
    public ushort Selected => LVGL.GetDropdownSelected(this);
    public ushort OptionCount => LVGL.GetDropdownOptionCount(this);
    public void Open() => LVGL.OpenDropdown(this);
    public void Close() => LVGL.CloseDropdown(this);
    public bool IsOpen => LVGL.IsDropdownOpen(this);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVRoller
{
    public readonly LVObject Object;
    public LVRoller(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetOptions(byte* options, byte mode = LV_ROLLER_MODE_NORMAL) => LVGL.SetRollerOptions(this, options, mode);
    public void SetOptions(ReadOnlySpan<byte> options, byte mode = LV_ROLLER_MODE_NORMAL) => LVGL.SetRollerOptions(this, options, mode);
    public void SetSelected(ushort index, bool animate = false) => LVGL.SetRollerSelected(this, index, animate ? 1 : 0);
    public ushort Selected => LVGL.GetRollerSelected(this);
    public void SetVisibleRowCount(byte count) => LVGL.SetRollerVisibleRowCount(this, count);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVTextArea
{
    public readonly LVObject Object;
    public LVTextArea(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetText(byte* text) => LVGL.SetTextAreaText(this, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetTextAreaText(this, text);
    public void AddText(byte* text) => LVGL.AddTextAreaText(this, text);
    public void AddText(ReadOnlySpan<byte> text) => LVGL.AddTextAreaText(this, text);
    public void DeleteCharacter() => LVGL.DeleteTextAreaCharacter(this);
    public void SetPlaceholderText(byte* text) => LVGL.SetTextAreaPlaceholderText(this, text);
    public void SetPlaceholderText(ReadOnlySpan<byte> text) => LVGL.SetTextAreaPlaceholderText(this, text);
    public void SetOneLine(bool value) => LVGL.SetTextAreaOneLine(this, (byte)(value ? 1 : 0));
    public void SetPasswordMode(bool value) => LVGL.SetTextAreaPasswordMode(this, (byte)(value ? 1 : 0));
    public void SetMaxLength(uint value) => LVGL.SetTextAreaMaxLength(this, value);
    public uint CursorPosition => LVGL.GetTextAreaCursorPosition(this);
    public void SetCursorPosition(int position) => LVGL.SetTextAreaCursorPosition(this, position);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVTable
{
    public readonly LVObject Object;
    public LVTable(LVObject objectHandle) => Object = objectHandle;
    public IntPtr Handle => Object.Handle;
    public void SetCellValue(ushort row, ushort column, byte* text) => LVGL.SetTableCellValue(this, row, column, text);
    public void SetCellValue(ushort row, ushort column, ReadOnlySpan<byte> text) => LVGL.SetTableCellValue(this, row, column, text);
    public void SetRowCount(ushort value) => LVGL.SetTableRowCount(this, value);
    public void SetColumnCount(ushort value) => LVGL.SetTableColumnCount(this, value);
    public void SetColumnWidth(ushort column, int width) => LVGL.SetTableColumnWidth(this, column, width);
    public ushort RowCount => LVGL.GetTableRowCount(this);
    public ushort ColumnCount => LVGL.GetTableColumnCount(this);
    public void AddEventCallback(delegate* unmanaged<LVEvent, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly struct LVEvent
{
    public readonly IntPtr Handle;
    public LVEvent(IntPtr handle) => Handle = handle;
    public uint Code => LVGL.GetEventCode(this);
    public LVObject Target => LVGL.GetEventTarget(this);
    public LVObject CurrentTarget => LVGL.GetEventCurrentTarget(this);
    public IntPtr Parameter => LVGL.GetEventParameter(this);
    public IntPtr UserData => LVGL.GetEventUserData(this);
    public uint Key => LVGL.GetEventKey(this);
    public void StopBubbling() => LVGL.StopEventBubbling(this);
    public void StopProcessing() => LVGL.StopEventProcessing(this);
}

public static unsafe class LVGL
{
    public const uint LV_EVENT_ALL = 0, LV_EVENT_PRESSED = 1, LV_EVENT_PRESSING = 2, LV_EVENT_CLICKED = 7, LV_EVENT_RELEASED = 8;
    public const uint LV_EVENT_VALUE_CHANGED = 28, LV_EVENT_READY = 31, LV_EVENT_CANCEL = 32, LV_EVENT_DELETE = 33;
    public const byte LV_DIR_NONE = 0, LV_DIR_HOR = 3, LV_DIR_VER = 12, LV_DIR_ALL = 15;
    public const uint LV_STATE_CHECKED = 1, LV_STATE_PRESSED = 1U << 5, LV_STATE_DISABLED = 1U << 7, LV_STATE_ANY = 0xFFFF;
    public const uint LV_OBJ_FLAG_HIDDEN = 1, LV_OBJ_FLAG_CLICKABLE = 1U << 1, LV_OBJ_FLAG_CHECKABLE = 1U << 3;
    public const uint LV_OBJ_FLAG_SCROLLABLE = 1U << 4, LV_OBJ_FLAG_EVENT_BUBBLE = 1U << 14, LV_OBJ_FLAG_GESTURE_BUBBLE = 1U << 15;
    public const byte LV_BAR_MODE_NORMAL = 0, LV_BAR_MODE_SYMMETRICAL = 1, LV_BAR_MODE_RANGE = 2;
    public const byte LV_ROLLER_MODE_NORMAL = 0, LV_ROLLER_MODE_INFINITE = 1;
    public const int LV_ANIM_OFF = 0;
    public const uint LV_PART_MAIN = 0, LV_PART_INDICATOR = 0x020000, LV_PART_KNOB = 0x030000;
    public const uint LV_DROPDOWN_POS_LAST = 0xFFFF;
    public static int Percentage(int value) => (value < 0 ? 1000 - value : value) | (1 << 13);

    public static LVObject CreateObject(LVObject parent) => new LVObject(lv_obj_create(parent.Handle));
    public static LVObject CreateScreen() => new LVObject(lv_obj_create(IntPtr.Zero));
    public static void LoadScreen(LVObject screen) => lv_disp_load_scr(screen.Handle);
    public static LVLabel CreateLabel(LVObject parent) => new LVLabel(new LVObject(lv_label_create(parent.Handle)));
    public static LVButton CreateButton(LVObject parent) => new LVButton(new LVObject(lv_btn_create(parent.Handle)));
    public static LVCheckbox CreateCheckbox(LVObject parent) => new LVCheckbox(new LVObject(lv_checkbox_create(parent.Handle)));
    public static LVSwitch CreateSwitch(LVObject parent) => new LVSwitch(new LVObject(lv_switch_create(parent.Handle)));
    public static LVBar CreateBar(LVObject parent) => new LVBar(new LVObject(lv_bar_create(parent.Handle)));
    public static LVSlider CreateSlider(LVObject parent) => new LVSlider(new LVObject(lv_slider_create(parent.Handle)));
    public static LVArc CreateArc(LVObject parent) => new LVArc(new LVObject(lv_arc_create(parent.Handle)));
    public static LVDropdown CreateDropdown(LVObject parent) => new LVDropdown(new LVObject(lv_dropdown_create(parent.Handle)));
    public static LVRoller CreateRoller(LVObject parent) => new LVRoller(new LVObject(lv_roller_create(parent.Handle)));
    public static LVTextArea CreateTextArea(LVObject parent) => new LVTextArea(new LVObject(lv_textarea_create(parent.Handle)));
    public static LVTable CreateTable(LVObject parent) => new LVTable(new LVObject(lv_table_create(parent.Handle)));

    internal static void SetPosition(LVObject o, int x, int y) => lv_obj_set_pos(o.Handle, x, y);
    internal static void SetX(LVObject o, int v) => lv_obj_set_x(o.Handle, v);
    internal static void SetY(LVObject o, int v) => lv_obj_set_y(o.Handle, v);
    internal static void SetSize(LVObject o, int w, int h) => lv_obj_set_size(o.Handle, w, h);
    internal static void SetWidth(LVObject o, int v) => lv_obj_set_width(o.Handle, v);
    internal static void SetHeight(LVObject o, int v) => lv_obj_set_height(o.Handle, v);
    internal static void Align(LVObject o, int a, int x, int y) => lv_obj_align(o.Handle, a, x, y);
    internal static void AlignTo(LVObject o, LVObject b, int a, int x, int y) => lv_obj_align_to(o.Handle, b.Handle, a, x, y);
    internal static void UpdateLayout(LVObject o) => lv_obj_update_layout(o.Handle);
    internal static int GetX(LVObject o) => lv_obj_get_x(o.Handle);
    internal static int GetY(LVObject o) => lv_obj_get_y(o.Handle);
    internal static int GetWidth(LVObject o) => lv_obj_get_width(o.Handle);
    internal static int GetHeight(LVObject o) => lv_obj_get_height(o.Handle);
    internal static LVObject GetParent(LVObject o) => new LVObject(lv_obj_get_parent(o.Handle));
    internal static void SetStylePadLeft(LVObject o, int v, uint s) => lv_obj_set_style_pad_left(o.Handle, v, s);
    internal static void SetStylePadRight(LVObject o, int v, uint s) => lv_obj_set_style_pad_right(o.Handle, v, s);
    internal static void SetStylePadTop(LVObject o, int v, uint s) => lv_obj_set_style_pad_top(o.Handle, v, s);
    internal static void SetStylePadBottom(LVObject o, int v, uint s) => lv_obj_set_style_pad_bottom(o.Handle, v, s);
    internal static void SetStylePadAll(LVObject o, int v, uint s)
    {
        SetStylePadLeft(o, v, s);
        SetStylePadRight(o, v, s);
        SetStylePadTop(o, v, s);
        SetStylePadBottom(o, v, s);
    }
    internal static void SetStyleArcOpacity(LVObject o, byte value, uint selector) => lv_obj_set_style_arc_opa(o.Handle, value, selector);
    internal static void SetStyleArcColor(LVObject o, uint color, uint selector) => lv_obj_set_style_arc_color(o.Handle, Color565(color), selector);
    internal static void SetStyleBackgroundColor(LVObject o, uint color, uint selector) => lv_obj_set_style_bg_color(o.Handle, Color565(color), selector);
    internal static void SetStyleShadowWidth(LVObject o, int value, uint selector) => lv_obj_set_style_shadow_width(o.Handle, value, selector);
    internal static void SetStyleShadowOpacity(LVObject o, byte value, uint selector) => lv_obj_set_style_shadow_opa(o.Handle, value, selector);
    internal static void SetStyleShadowOffsetY(LVObject o, int value, uint selector) => lv_obj_set_style_shadow_ofs_y(o.Handle, value, selector);
    internal static void SetStyleThemeFontLarge(LVObject o, uint selector) => lv_obj_set_style_text_font(o.Handle, lv_theme_get_font_large(o.Handle), selector);
    private static ushort Color565(uint color) => (ushort)(((color & 0xF80000u) >> 8) | ((color & 0xFC00u) >> 5) | ((color & 0xF8u) >> 3));
    internal static void AddFlag(LVObject o, uint f) => lv_obj_add_flag(o.Handle, f);
    internal static void ClearFlag(LVObject o, uint f) => lv_obj_clear_flag(o.Handle, f);
    internal static bool HasFlag(LVObject o, uint f) => lv_obj_has_flag(o.Handle, f) != 0;
    internal static void AddState(LVObject o, uint s) => lv_obj_add_state(o.Handle, s);
    internal static void ClearState(LVObject o, uint s) => lv_obj_clear_state(o.Handle, s);
    internal static bool HasState(LVObject o, uint s) => lv_obj_has_state(o.Handle, s) != 0;
    internal static void SetChecked(LVObject o, byte v) { if (v != 0) AddState(o, LV_STATE_CHECKED); else ClearState(o, LV_STATE_CHECKED); }
    internal static void SetExtClickArea(LVObject o, int v) => lv_obj_set_ext_click_area(o.Handle, v);
    internal static void ScrollTo(LVObject o, int x, int y, int a) => lv_obj_scroll_to(o.Handle, x, y, a);
    internal static void ScrollBy(LVObject o, int x, int y, int a) => lv_obj_scroll_by(o.Handle, x, y, a);
    internal static void SetScrollDirection(LVObject o, byte direction) => lv_obj_set_scroll_dir(o.Handle, direction);
    internal static void Invalidate(LVObject o) => lv_obj_invalidate(o.Handle);

    internal static void SetLabelLongMode(LVLabel o, byte v) => lv_label_set_long_mode(o.Handle, v);
    internal static void SetLabelRecolor(LVLabel o, byte v) => lv_label_set_recolor(o.Handle, v);
    internal static void SetLabelText(LVLabel o, byte* t) => lv_label_set_text(o.Handle, (sbyte*)t);
    internal static void SetLabelText(LVLabel o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetLabelText(o, p); }
    internal static void SetCheckboxText(LVCheckbox o, byte* t) => lv_checkbox_set_text(o.Handle, (sbyte*)t);
    internal static void SetCheckboxText(LVCheckbox o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetCheckboxText(o, p); }
    internal static void SetTextAreaText(LVTextArea o, byte* t) => lv_textarea_set_text(o.Handle, (sbyte*)t);
    internal static void SetTextAreaText(LVTextArea o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetTextAreaText(o, p); }
    internal static void AddTextAreaText(LVTextArea o, byte* t) => lv_textarea_add_text(o.Handle, (sbyte*)t);
    internal static void AddTextAreaText(LVTextArea o, ReadOnlySpan<byte> t) { fixed (byte* p = t) AddTextAreaText(o, p); }
    internal static void DeleteTextAreaCharacter(LVTextArea o) => lv_textarea_del_char(o.Handle);
    internal static void SetTextAreaPlaceholderText(LVTextArea o, byte* t) => lv_textarea_set_placeholder_text(o.Handle, (sbyte*)t);
    internal static void SetTextAreaPlaceholderText(LVTextArea o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetTextAreaPlaceholderText(o, p); }
    internal static void SetTextAreaOneLine(LVTextArea o, byte v) => lv_textarea_set_one_line(o.Handle, v);
    internal static void SetTextAreaPasswordMode(LVTextArea o, byte v) => lv_textarea_set_password_mode(o.Handle, v);
    internal static void SetTextAreaMaxLength(LVTextArea o, uint v) => lv_textarea_set_max_length(o.Handle, v);
    internal static uint GetTextAreaCursorPosition(LVTextArea o) => lv_textarea_get_cursor_pos(o.Handle);
    internal static void SetTextAreaCursorPosition(LVTextArea o, int v) => lv_textarea_set_cursor_pos(o.Handle, v);

    internal static void SetBarRange(LVObject o, int min, int max) => lv_bar_set_range(o.Handle, min, max);
    internal static void SetBarValue(LVObject o, int v, int a) => lv_bar_set_value(o.Handle, v, a);
    internal static void SetBarStartValue(LVObject o, int v, int a) => lv_bar_set_start_value(o.Handle, v, a);
    internal static int GetBarValue(LVObject o) => lv_bar_get_value(o.Handle);
    internal static int GetBarStartValue(LVObject o) => lv_bar_get_start_value(o.Handle);
    internal static int GetBarMinimum(LVObject o) => lv_bar_get_min_value(o.Handle);
    internal static int GetBarMaximum(LVObject o) => lv_bar_get_max_value(o.Handle);
    internal static void SetBarMode(LVObject o, byte v) => lv_bar_set_mode(o.Handle, v);
    internal static bool IsSliderDragged(LVSlider o) => lv_slider_is_dragged(o.Handle) != 0;
    internal static void SetArcAngles(LVArc o, ushort s, ushort e) => lv_arc_set_angles(o.Handle, s, e);
    internal static void SetArcBackgroundAngles(LVArc o, ushort s, ushort e) => lv_arc_set_bg_angles(o.Handle, s, e);
    internal static void SetArcRotation(LVArc o, ushort v) => lv_arc_set_rotation(o.Handle, v);
    internal static void SetArcRange(LVArc o, short min, short max) => lv_arc_set_range(o.Handle, min, max);
    internal static void SetArcValue(LVArc o, short v) => lv_arc_set_value(o.Handle, v);
    internal static short GetArcValue(LVArc o) => lv_arc_get_value(o.Handle);

    internal static void SetDropdownOptions(LVDropdown o, byte* t) => lv_dropdown_set_options(o.Handle, (sbyte*)t);
    internal static void SetDropdownOptions(LVDropdown o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetDropdownOptions(o, p); }
    internal static void SetDropdownText(LVDropdown o, byte* t) => lv_dropdown_set_text(o.Handle, (sbyte*)t);
    internal static void SetDropdownText(LVDropdown o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetDropdownText(o, p); }
    internal static void SetDropdownSelected(LVDropdown o, ushort v) => lv_dropdown_set_selected(o.Handle, v);
    internal static ushort GetDropdownSelected(LVDropdown o) => lv_dropdown_get_selected(o.Handle);
    internal static ushort GetDropdownOptionCount(LVDropdown o) => lv_dropdown_get_option_cnt(o.Handle);
    internal static void OpenDropdown(LVDropdown o) => lv_dropdown_open(o.Handle);
    internal static void CloseDropdown(LVDropdown o) => lv_dropdown_close(o.Handle);
    internal static bool IsDropdownOpen(LVDropdown o) => lv_dropdown_is_open(o.Handle) != 0;
    internal static void SetRollerOptions(LVRoller o, byte* t, byte m) => lv_roller_set_options(o.Handle, (sbyte*)t, m);
    internal static void SetRollerOptions(LVRoller o, ReadOnlySpan<byte> t, byte m) { fixed (byte* p = t) SetRollerOptions(o, p, m); }
    internal static void SetRollerSelected(LVRoller o, ushort v, int a) => lv_roller_set_selected(o.Handle, v, a);
    internal static ushort GetRollerSelected(LVRoller o) => lv_roller_get_selected(o.Handle);
    internal static void SetRollerVisibleRowCount(LVRoller o, byte v) => lv_roller_set_visible_row_count(o.Handle, v);
    internal static void SetTableCellValue(LVTable o, ushort r, ushort c, byte* t) => lv_table_set_cell_value(o.Handle, r, c, (sbyte*)t);
    internal static void SetTableCellValue(LVTable o, ushort r, ushort c, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetTableCellValue(o, r, c, p); }
    internal static void SetTableRowCount(LVTable o, ushort v) => lv_table_set_row_cnt(o.Handle, v);
    internal static void SetTableColumnCount(LVTable o, ushort v) => lv_table_set_col_cnt(o.Handle, v);
    internal static void SetTableColumnWidth(LVTable o, ushort c, int w) => lv_table_set_col_width(o.Handle, c, w);
    internal static ushort GetTableRowCount(LVTable o) => lv_table_get_row_cnt(o.Handle);
    internal static ushort GetTableColumnCount(LVTable o) => lv_table_get_col_cnt(o.Handle);

    internal static void AddEventCallback(LVObject o, delegate* unmanaged<LVEvent, void> cb, uint f, IntPtr d) => lv_obj_add_event_cb(o.Handle, cb, f, d);
    internal static uint GetEventCode(LVEvent e) => lv_event_get_code(e.Handle);
    internal static LVObject GetEventTarget(LVEvent e) => new LVObject(lv_event_get_target(e.Handle));
    internal static LVObject GetEventCurrentTarget(LVEvent e) => new LVObject(lv_event_get_current_target(e.Handle));
    internal static IntPtr GetEventParameter(LVEvent e) => lv_event_get_param(e.Handle);
    internal static IntPtr GetEventUserData(LVEvent e) => lv_event_get_user_data(e.Handle);
    internal static uint GetEventKey(LVEvent e) => lv_event_get_key(e.Handle);
    internal static void StopEventBubbling(LVEvent e) => lv_event_stop_bubbling(e.Handle);
    internal static void StopEventProcessing(LVEvent e) => lv_event_stop_processing(e.Handle);

    [DllImport("*", EntryPoint = "lv_obj_create")] private static extern IntPtr lv_obj_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_disp_load_scr")] private static extern void lv_disp_load_scr(IntPtr screen);
    [DllImport("*", EntryPoint = "lv_obj_set_pos")] private static extern void lv_obj_set_pos(IntPtr o, int x, int y);
    [DllImport("*", EntryPoint = "lv_obj_set_x")] private static extern void lv_obj_set_x(IntPtr o, int v);
    [DllImport("*", EntryPoint = "lv_obj_set_y")] private static extern void lv_obj_set_y(IntPtr o, int v);
    [DllImport("*", EntryPoint = "lv_obj_set_size")] private static extern void lv_obj_set_size(IntPtr o, int w, int h);
    [DllImport("*", EntryPoint = "lv_obj_set_width")] private static extern void lv_obj_set_width(IntPtr o, int v);
    [DllImport("*", EntryPoint = "lv_obj_set_height")] private static extern void lv_obj_set_height(IntPtr o, int v);
    [DllImport("*", EntryPoint = "lv_obj_align")] private static extern void lv_obj_align(IntPtr o, int a, int x, int y);
    [DllImport("*", EntryPoint = "lv_obj_align_to")] private static extern void lv_obj_align_to(IntPtr o, IntPtr b, int a, int x, int y);
    [DllImport("*", EntryPoint = "lv_obj_update_layout")] private static extern void lv_obj_update_layout(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_set_style_arc_opa")] private static extern void lv_obj_set_style_arc_opa(IntPtr o, byte value, uint selector);
    [DllImport("*", EntryPoint = "lv_obj_set_style_arc_color")] private static extern void lv_obj_set_style_arc_color(IntPtr o, ushort color, uint selector);
    [DllImport("*", EntryPoint = "lv_obj_set_style_bg_color")] private static extern void lv_obj_set_style_bg_color(IntPtr o, ushort color, uint selector);
    [DllImport("*", EntryPoint = "lv_obj_set_style_shadow_width")] private static extern void lv_obj_set_style_shadow_width(IntPtr o, int value, uint selector);
    [DllImport("*", EntryPoint = "lv_obj_set_style_shadow_opa")] private static extern void lv_obj_set_style_shadow_opa(IntPtr o, byte value, uint selector);
    [DllImport("*", EntryPoint = "lv_obj_set_style_shadow_ofs_y")] private static extern void lv_obj_set_style_shadow_ofs_y(IntPtr o, int value, uint selector);
    [DllImport("*", EntryPoint = "lv_obj_set_style_text_font")] private static extern void lv_obj_set_style_text_font(IntPtr o, IntPtr font, uint selector);
    [DllImport("*", EntryPoint = "lv_theme_get_font_large")] private static extern IntPtr lv_theme_get_font_large(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_get_x")] private static extern int lv_obj_get_x(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_get_y")] private static extern int lv_obj_get_y(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_get_width")] private static extern int lv_obj_get_width(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_get_height")] private static extern int lv_obj_get_height(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_get_parent")] private static extern IntPtr lv_obj_get_parent(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_set_style_pad_left")] private static extern void lv_obj_set_style_pad_left(IntPtr o, int v, uint s);
    [DllImport("*", EntryPoint = "lv_obj_set_style_pad_right")] private static extern void lv_obj_set_style_pad_right(IntPtr o, int v, uint s);
    [DllImport("*", EntryPoint = "lv_obj_set_style_pad_top")] private static extern void lv_obj_set_style_pad_top(IntPtr o, int v, uint s);
    [DllImport("*", EntryPoint = "lv_obj_set_style_pad_bottom")] private static extern void lv_obj_set_style_pad_bottom(IntPtr o, int v, uint s);
    [DllImport("*", EntryPoint = "lv_obj_add_flag")] private static extern void lv_obj_add_flag(IntPtr o, uint f);
    [DllImport("*", EntryPoint = "lv_obj_clear_flag")] private static extern void lv_obj_clear_flag(IntPtr o, uint f);
    [DllImport("*", EntryPoint = "lv_obj_has_flag")] private static extern byte lv_obj_has_flag(IntPtr o, uint f);
    [DllImport("*", EntryPoint = "lv_obj_add_state")] private static extern void lv_obj_add_state(IntPtr o, uint s);
    [DllImport("*", EntryPoint = "lv_obj_clear_state")] private static extern void lv_obj_clear_state(IntPtr o, uint s);
    [DllImport("*", EntryPoint = "lv_obj_has_state")] private static extern byte lv_obj_has_state(IntPtr o, uint s);
    [DllImport("*", EntryPoint = "lv_obj_set_ext_click_area")] private static extern void lv_obj_set_ext_click_area(IntPtr o, int v);
    [DllImport("*", EntryPoint = "lv_obj_scroll_to")] private static extern void lv_obj_scroll_to(IntPtr o, int x, int y, int a);
    [DllImport("*", EntryPoint = "lv_obj_scroll_by")] private static extern void lv_obj_scroll_by(IntPtr o, int x, int y, int a);
    [DllImport("*", EntryPoint = "lv_obj_set_scroll_dir")] private static extern void lv_obj_set_scroll_dir(IntPtr o, byte direction);
    [DllImport("*", EntryPoint = "lv_obj_invalidate")] private static extern void lv_obj_invalidate(IntPtr o);
    [DllImport("*", EntryPoint = "lv_label_create")] private static extern IntPtr lv_label_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_label_set_long_mode")] private static extern void lv_label_set_long_mode(IntPtr o, byte v);
    [DllImport("*", EntryPoint = "lv_label_set_recolor")] private static extern void lv_label_set_recolor(IntPtr o, byte v);
    [DllImport("*", EntryPoint = "lv_label_set_text")] private static extern void lv_label_set_text(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_btn_create")] private static extern IntPtr lv_btn_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_checkbox_create")] private static extern IntPtr lv_checkbox_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_checkbox_set_text")] private static extern void lv_checkbox_set_text(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_switch_create")] private static extern IntPtr lv_switch_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_bar_create")] private static extern IntPtr lv_bar_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_bar_set_range")] private static extern void lv_bar_set_range(IntPtr o, int min, int max);
    [DllImport("*", EntryPoint = "lv_bar_set_value")] private static extern void lv_bar_set_value(IntPtr o, int v, int a);
    [DllImport("*", EntryPoint = "lv_bar_set_start_value")] private static extern void lv_bar_set_start_value(IntPtr o, int v, int a);
    [DllImport("*", EntryPoint = "lv_bar_get_value")] private static extern int lv_bar_get_value(IntPtr o);
    [DllImport("*", EntryPoint = "lv_bar_get_start_value")] private static extern int lv_bar_get_start_value(IntPtr o);
    [DllImport("*", EntryPoint = "lv_bar_get_min_value")] private static extern int lv_bar_get_min_value(IntPtr o);
    [DllImport("*", EntryPoint = "lv_bar_get_max_value")] private static extern int lv_bar_get_max_value(IntPtr o);
    [DllImport("*", EntryPoint = "lv_bar_set_mode")] private static extern void lv_bar_set_mode(IntPtr o, byte v);
    [DllImport("*", EntryPoint = "lv_slider_create")] private static extern IntPtr lv_slider_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_slider_is_dragged")] private static extern byte lv_slider_is_dragged(IntPtr o);
    [DllImport("*", EntryPoint = "lv_arc_create")] private static extern IntPtr lv_arc_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_arc_set_angles")] private static extern void lv_arc_set_angles(IntPtr o, ushort s, ushort e);
    [DllImport("*", EntryPoint = "lv_arc_set_bg_angles")] private static extern void lv_arc_set_bg_angles(IntPtr o, ushort s, ushort e);
    [DllImport("*", EntryPoint = "lv_arc_set_rotation")] private static extern void lv_arc_set_rotation(IntPtr o, ushort v);
    [DllImport("*", EntryPoint = "lv_arc_set_range")] private static extern void lv_arc_set_range(IntPtr o, short min, short max);
    [DllImport("*", EntryPoint = "lv_arc_set_value")] private static extern void lv_arc_set_value(IntPtr o, short v);
    [DllImport("*", EntryPoint = "lv_arc_get_value")] private static extern short lv_arc_get_value(IntPtr o);
    [DllImport("*", EntryPoint = "lv_dropdown_create")] private static extern IntPtr lv_dropdown_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_dropdown_set_options")] private static extern void lv_dropdown_set_options(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_dropdown_set_text")] private static extern void lv_dropdown_set_text(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_dropdown_set_selected")] private static extern void lv_dropdown_set_selected(IntPtr o, ushort v);
    [DllImport("*", EntryPoint = "lv_dropdown_get_selected")] private static extern ushort lv_dropdown_get_selected(IntPtr o);
    [DllImport("*", EntryPoint = "lv_dropdown_get_option_cnt")] private static extern ushort lv_dropdown_get_option_cnt(IntPtr o);
    [DllImport("*", EntryPoint = "lv_dropdown_open")] private static extern void lv_dropdown_open(IntPtr o);
    [DllImport("*", EntryPoint = "lv_dropdown_close")] private static extern void lv_dropdown_close(IntPtr o);
    [DllImport("*", EntryPoint = "lv_dropdown_is_open")] private static extern byte lv_dropdown_is_open(IntPtr o);
    [DllImport("*", EntryPoint = "lv_roller_create")] private static extern IntPtr lv_roller_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_roller_set_options")] private static extern void lv_roller_set_options(IntPtr o, sbyte* t, byte m);
    [DllImport("*", EntryPoint = "lv_roller_set_selected")] private static extern void lv_roller_set_selected(IntPtr o, ushort v, int a);
    [DllImport("*", EntryPoint = "lv_roller_get_selected")] private static extern ushort lv_roller_get_selected(IntPtr o);
    [DllImport("*", EntryPoint = "lv_roller_set_visible_row_count")] private static extern void lv_roller_set_visible_row_count(IntPtr o, byte v);
    [DllImport("*", EntryPoint = "lv_textarea_create")] private static extern IntPtr lv_textarea_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_textarea_set_text")] private static extern void lv_textarea_set_text(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_textarea_add_text")] private static extern void lv_textarea_add_text(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_textarea_del_char")] private static extern void lv_textarea_del_char(IntPtr o);
    [DllImport("*", EntryPoint = "lv_textarea_set_placeholder_text")] private static extern void lv_textarea_set_placeholder_text(IntPtr o, sbyte* t);
    [DllImport("*", EntryPoint = "lv_textarea_set_one_line")] private static extern void lv_textarea_set_one_line(IntPtr o, byte v);
    [DllImport("*", EntryPoint = "lv_textarea_set_password_mode")] private static extern void lv_textarea_set_password_mode(IntPtr o, byte v);
    [DllImport("*", EntryPoint = "lv_textarea_set_max_length")] private static extern void lv_textarea_set_max_length(IntPtr o, uint v);
    [DllImport("*", EntryPoint = "lv_textarea_get_cursor_pos")] private static extern uint lv_textarea_get_cursor_pos(IntPtr o);
    [DllImport("*", EntryPoint = "lv_textarea_set_cursor_pos")] private static extern void lv_textarea_set_cursor_pos(IntPtr o, int v);
    [DllImport("*", EntryPoint = "lv_table_create")] private static extern IntPtr lv_table_create(IntPtr p);
    [DllImport("*", EntryPoint = "lv_table_set_cell_value")] private static extern void lv_table_set_cell_value(IntPtr o, ushort r, ushort c, sbyte* t);
    [DllImport("*", EntryPoint = "lv_table_set_row_cnt")] private static extern void lv_table_set_row_cnt(IntPtr o, ushort v);
    [DllImport("*", EntryPoint = "lv_table_set_col_cnt")] private static extern void lv_table_set_col_cnt(IntPtr o, ushort v);
    [DllImport("*", EntryPoint = "lv_table_set_col_width")] private static extern void lv_table_set_col_width(IntPtr o, ushort c, int w);
    [DllImport("*", EntryPoint = "lv_table_get_row_cnt")] private static extern ushort lv_table_get_row_cnt(IntPtr o);
    [DllImport("*", EntryPoint = "lv_table_get_col_cnt")] private static extern ushort lv_table_get_col_cnt(IntPtr o);
    [DllImport("*", EntryPoint = "lv_obj_add_event_cb")] private static extern IntPtr lv_obj_add_event_cb(IntPtr o, delegate* unmanaged<LVEvent, void> cb, uint f, IntPtr d);
    [DllImport("*", EntryPoint = "lv_event_get_code")] private static extern uint lv_event_get_code(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_target")] private static extern IntPtr lv_event_get_target(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_current_target")] private static extern IntPtr lv_event_get_current_target(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_param")] private static extern IntPtr lv_event_get_param(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_user_data")] private static extern IntPtr lv_event_get_user_data(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_key")] private static extern uint lv_event_get_key(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_stop_bubbling")] private static extern void lv_event_stop_bubbling(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_stop_processing")] private static extern void lv_event_stop_processing(IntPtr e);
}
