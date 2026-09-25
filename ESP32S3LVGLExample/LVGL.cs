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
    public int X => LVGL.GetX(Handle);
    public int Y => LVGL.GetY(Handle);
    public int Width => LVGL.GetWidth(Handle);
    public int Height => LVGL.GetHeight(Handle);
    public LVObject Parent => new LVObject(LVGL.GetParent(Handle));
    public void SetPosition(int x, int y) => LVGL.SetPosition(Handle, x, y);
    public void SetX(int value) => LVGL.SetX(Handle, value);
    public void SetY(int value) => LVGL.SetY(Handle, value);
    public void SetSize(int width, int height) => LVGL.SetSize(Handle, width, height);
    public void SetWidth(int value) => LVGL.SetWidth(Handle, value);
    public void SetHeight(int value) => LVGL.SetHeight(Handle, value);
    public void SetSizePercent(int width, int height) => LVGL.SetSize(Handle, LVGL.Percentage(width), LVGL.Percentage(height));
    public void SetWidthPercent(int value) => LVGL.SetWidth(Handle, LVGL.Percentage(value));
    public void SetHeightPercent(int value) => LVGL.SetHeight(Handle, LVGL.Percentage(value));
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => LVGL.Align(Handle, (int)align, xOffset, yOffset);
    public void AlignTo(LVObject baseObject, LVAlign align, int xOffset = 0, int yOffset = 0) => LVGL.AlignTo(Handle, baseObject.Handle, (int)align, xOffset, yOffset);
    public void Center() => Align(LVAlign.Center);
    public void UpdateLayout() => LVGL.UpdateLayout(Handle);
    public void SetStylePadLeft(int value, uint selector = 0) => LVGL.SetStylePadLeft(Handle, value, selector);
    public void SetStylePadRight(int value, uint selector = 0) => LVGL.SetStylePadRight(Handle, value, selector);
    public void SetStylePadTop(int value, uint selector = 0) => LVGL.SetStylePadTop(Handle, value, selector);
    public void SetStylePadBottom(int value, uint selector = 0) => LVGL.SetStylePadBottom(Handle, value, selector);
    public void SetStylePadAll(int value, uint selector = 0) => LVGL.SetStylePadAll(Handle, value, selector);
    public void ClearFlag(uint flag) => LVGL.ClearFlag(Handle, flag);
    public void AddFlag(uint flag) => LVGL.AddFlag(Handle, flag);
    public bool HasFlag(uint flag) => LVGL.HasFlag(Handle, flag);
    public void AddState(uint state) => LVGL.AddState(Handle, state);
    public void ClearState(uint state) => LVGL.ClearState(Handle, state);
    public bool HasState(uint state) => LVGL.HasState(Handle, state);
    public void SetExtClickArea(int value) => LVGL.SetExtClickArea(Handle, value);
    public void ScrollTo(int x, int y, bool animate = false) => LVGL.ScrollTo(Handle, x, y, animate ? 1 : 0);
    public void ScrollBy(int x, int y, bool animate = false) => LVGL.ScrollBy(Handle, x, y, animate ? 1 : 0);
    public void Invalidate() => LVGL.Invalidate(Handle);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => LVGL.AddEventCallback(Handle, callback, filter, userData);
}

public readonly unsafe struct LVLabel
{
    public readonly LVObject Object;
    public LVLabel(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetSize(int width, int height) => Object.SetSize(width, height);
    public void SetSizePercent(int width, int height) => Object.SetSizePercent(width, height);
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => Object.Align(align, xOffset, yOffset);
    public void SetLongMode(byte mode) => LVGL.SetLabelLongMode(Handle, mode);
    public void SetRecolor(bool enabled) => LVGL.SetLabelRecolor(Handle, (byte)(enabled ? 1 : 0));
    public void SetText(byte* text) => LVGL.SetLabelText(Handle, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetLabelText(Handle, text);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVButton
{
    public readonly LVObject Object;
    public LVButton(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetSize(int width, int height) => Object.SetSize(width, height);
    public void SetSizePercent(int width, int height) => Object.SetSizePercent(width, height);
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => Object.Align(align, xOffset, yOffset);
    public LVLabel CreateLabel() => LVGL.CreateLabel(Object);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVCheckbox
{
    public readonly LVObject Object;
    public LVCheckbox(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetText(byte* text) => LVGL.SetCheckboxText(Handle, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetCheckboxText(Handle, text);
    public void SetChecked(bool value) => LVGL.SetChecked(Handle, (byte)(value ? 1 : 0));
    public bool IsChecked => LVGL.HasState(Handle, LV_STATE_CHECKED);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVSwitch
{
    public readonly LVObject Object;
    public LVSwitch(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetChecked(bool value) => LVGL.SetChecked(Handle, (byte)(value ? 1 : 0));
    public bool IsChecked => LVGL.HasState(Handle, LV_STATE_CHECKED);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVBar
{
    public readonly LVObject Object;
    public LVBar(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetRange(int minimum, int maximum) => LVGL.SetBarRange(Handle, minimum, maximum);
    public void SetValue(int value, bool animate = false) => LVGL.SetBarValue(Handle, value, animate ? 1 : 0);
    public void SetStartValue(int value, bool animate = false) => LVGL.SetBarStartValue(Handle, value, animate ? 1 : 0);
    public int Value => LVGL.GetBarValue(Handle);
    public int StartValue => LVGL.GetBarStartValue(Handle);
    public int Minimum => LVGL.GetBarMinimum(Handle);
    public int Maximum => LVGL.GetBarMaximum(Handle);
    public void SetMode(byte mode) => LVGL.SetBarMode(Handle, mode);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVSlider
{
    public readonly LVObject Object;
    public LVSlider(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetSize(int width, int height) => Object.SetSize(width, height);
    public void SetSizePercent(int width, int height) => Object.SetSizePercent(width, height);
    public void Align(LVAlign align, int xOffset = 0, int yOffset = 0) => Object.Align(align, xOffset, yOffset);
    public void SetRange(int minimum, int maximum) => LVGL.SetBarRange(Handle, minimum, maximum);
    public void SetValue(int value, bool animate = false) => LVGL.SetBarValue(Handle, value, animate ? 1 : 0);
    public void SetLeftValue(int value, bool animate = false) => LVGL.SetBarStartValue(Handle, value, animate ? 1 : 0);
    public int GetValue() => LVGL.GetBarValue(Handle);
    public int GetLeftValue() => LVGL.GetBarStartValue(Handle);
    public bool IsDragged => LVGL.IsSliderDragged(Handle);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVArc
{
    public readonly LVObject Object;
    public LVArc(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetAngles(ushort start, ushort end) => LVGL.SetArcAngles(Handle, start, end);
    public void SetBackgroundAngles(ushort start, ushort end) => LVGL.SetArcBackgroundAngles(Handle, start, end);
    public void SetRotation(ushort rotation) => LVGL.SetArcRotation(Handle, rotation);
    public void SetRange(short minimum, short maximum) => LVGL.SetArcRange(Handle, minimum, maximum);
    public void SetValue(short value) => LVGL.SetArcValue(Handle, value);
    public short Value => LVGL.GetArcValue(Handle);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVDropdown
{
    public readonly LVObject Object;
    public LVDropdown(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetOptions(byte* options) => LVGL.SetDropdownOptions(Handle, options);
    public void SetOptions(ReadOnlySpan<byte> options) => LVGL.SetDropdownOptions(Handle, options);
    public void SetText(byte* text) => LVGL.SetDropdownText(Handle, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetDropdownText(Handle, text);
    public void SetSelected(ushort index) => LVGL.SetDropdownSelected(Handle, index);
    public ushort Selected => LVGL.GetDropdownSelected(Handle);
    public ushort OptionCount => LVGL.GetDropdownOptionCount(Handle);
    public void Open() => LVGL.OpenDropdown(Handle);
    public void Close() => LVGL.CloseDropdown(Handle);
    public bool IsOpen => LVGL.IsDropdownOpen(Handle);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVRoller
{
    public readonly LVObject Object;
    public LVRoller(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetOptions(byte* options, byte mode = LV_ROLLER_MODE_NORMAL) => LVGL.SetRollerOptions(Handle, options, mode);
    public void SetOptions(ReadOnlySpan<byte> options, byte mode = LV_ROLLER_MODE_NORMAL) => LVGL.SetRollerOptions(Handle, options, mode);
    public void SetSelected(ushort index, bool animate = false) => LVGL.SetRollerSelected(Handle, index, animate ? 1 : 0);
    public ushort Selected => LVGL.GetRollerSelected(Handle);
    public void SetVisibleRowCount(byte count) => LVGL.SetRollerVisibleRowCount(Handle, count);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVTextArea
{
    public readonly LVObject Object;
    public LVTextArea(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetText(byte* text) => LVGL.SetTextAreaText(Handle, text);
    public void SetText(ReadOnlySpan<byte> text) => LVGL.SetTextAreaText(Handle, text);
    public void AddText(byte* text) => LVGL.AddTextAreaText(Handle, text);
    public void AddText(ReadOnlySpan<byte> text) => LVGL.AddTextAreaText(Handle, text);
    public void DeleteCharacter() => LVGL.DeleteTextAreaCharacter(Handle);
    public void SetPlaceholderText(byte* text) => LVGL.SetTextAreaPlaceholderText(Handle, text);
    public void SetPlaceholderText(ReadOnlySpan<byte> text) => LVGL.SetTextAreaPlaceholderText(Handle, text);
    public void SetOneLine(bool value) => LVGL.SetTextAreaOneLine(Handle, (byte)(value ? 1 : 0));
    public void SetPasswordMode(bool value) => LVGL.SetTextAreaPasswordMode(Handle, (byte)(value ? 1 : 0));
    public void SetMaxLength(uint value) => LVGL.SetTextAreaMaxLength(Handle, value);
    public uint CursorPosition => LVGL.GetTextAreaCursorPosition(Handle);
    public void SetCursorPosition(int position) => LVGL.SetTextAreaCursorPosition(Handle, position);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly unsafe struct LVTable
{
    public readonly LVObject Object;
    public LVTable(IntPtr handle) => Object = new LVObject(handle);
    public IntPtr Handle => Object.Handle;
    public void SetCellValue(ushort row, ushort column, byte* text) => LVGL.SetTableCellValue(Handle, row, column, text);
    public void SetCellValue(ushort row, ushort column, ReadOnlySpan<byte> text) => LVGL.SetTableCellValue(Handle, row, column, text);
    public void SetRowCount(ushort value) => LVGL.SetTableRowCount(Handle, value);
    public void SetColumnCount(ushort value) => LVGL.SetTableColumnCount(Handle, value);
    public void SetColumnWidth(ushort column, int width) => LVGL.SetTableColumnWidth(Handle, column, width);
    public ushort RowCount => LVGL.GetTableRowCount(Handle);
    public ushort ColumnCount => LVGL.GetTableColumnCount(Handle);
    public void AddEventCallback(delegate* unmanaged<IntPtr, void> callback, uint filter = LV_EVENT_ALL, IntPtr userData = default) => Object.AddEventCallback(callback, filter, userData);
}

public readonly struct LVEvent
{
    public readonly IntPtr Handle;
    public LVEvent(IntPtr handle) => Handle = handle;
    public uint Code => LVGL.GetEventCode(Handle);
    public LVObject Target => new LVObject(LVGL.GetEventTarget(Handle));
    public LVObject CurrentTarget => new LVObject(LVGL.GetEventCurrentTarget(Handle));
    public IntPtr Parameter => LVGL.GetEventParameter(Handle);
    public IntPtr UserData => LVGL.GetEventUserData(Handle);
    public uint Key => LVGL.GetEventKey(Handle);
    public void StopBubbling() => LVGL.StopEventBubbling(Handle);
    public void StopProcessing() => LVGL.StopEventProcessing(Handle);
}

public static unsafe class LVGL
{
    public const uint LV_EVENT_ALL = 0, LV_EVENT_PRESSED = 1, LV_EVENT_PRESSING = 2, LV_EVENT_CLICKED = 7, LV_EVENT_RELEASED = 8;
    public const uint LV_EVENT_VALUE_CHANGED = 32, LV_EVENT_READY = 35, LV_EVENT_CANCEL = 36, LV_EVENT_DELETE = 37;
    public const uint LV_STATE_CHECKED = 1, LV_STATE_PRESSED = 1U << 5, LV_STATE_DISABLED = 1U << 7, LV_STATE_ANY = 0xFFFF;
    public const uint LV_OBJ_FLAG_HIDDEN = 1, LV_OBJ_FLAG_CLICKABLE = 1U << 1, LV_OBJ_FLAG_CHECKABLE = 1U << 3;
    public const uint LV_OBJ_FLAG_SCROLLABLE = 1U << 4, LV_OBJ_FLAG_EVENT_BUBBLE = 1U << 14, LV_OBJ_FLAG_GESTURE_BUBBLE = 1U << 15;
    public const byte LV_BAR_MODE_NORMAL = 0, LV_BAR_MODE_SYMMETRICAL = 1, LV_BAR_MODE_RANGE = 2;
    public const byte LV_ROLLER_MODE_NORMAL = 0, LV_ROLLER_MODE_INFINITE = 1;
    public const int LV_ANIM_OFF = 0;
    public const uint LV_DROPDOWN_POS_LAST = 0xFFFF;
    public static int Percentage(int value) => (value < 0 ? 1000 - value : value) | (1 << 13);

    public static LVObject CreateObject(LVObject parent) => new LVObject(lv_obj_create(parent.Handle));
    public static LVObject CreateScreen() => new LVObject(lv_obj_create(IntPtr.Zero));
    public static void LoadScreen(LVObject screen) => lv_disp_load_scr(screen.Handle);
    public static LVLabel CreateLabel(LVObject parent) => new LVLabel(lv_label_create(parent.Handle));
    public static LVButton CreateButton(LVObject parent) => new LVButton(lv_btn_create(parent.Handle));
    public static LVCheckbox CreateCheckbox(LVObject parent) => new LVCheckbox(lv_checkbox_create(parent.Handle));
    public static LVSwitch CreateSwitch(LVObject parent) => new LVSwitch(lv_switch_create(parent.Handle));
    public static LVBar CreateBar(LVObject parent) => new LVBar(lv_bar_create(parent.Handle));
    public static LVSlider CreateSlider(LVObject parent) => new LVSlider(lv_slider_create(parent.Handle));
    public static LVArc CreateArc(LVObject parent) => new LVArc(lv_arc_create(parent.Handle));
    public static LVDropdown CreateDropdown(LVObject parent) => new LVDropdown(lv_dropdown_create(parent.Handle));
    public static LVRoller CreateRoller(LVObject parent) => new LVRoller(lv_roller_create(parent.Handle));
    public static LVTextArea CreateTextArea(LVObject parent) => new LVTextArea(lv_textarea_create(parent.Handle));
    public static LVTable CreateTable(LVObject parent) => new LVTable(lv_table_create(parent.Handle));

    internal static void SetPosition(IntPtr o, int x, int y) => lv_obj_set_pos(o, x, y);
    internal static void SetX(IntPtr o, int v) => lv_obj_set_x(o, v);
    internal static void SetY(IntPtr o, int v) => lv_obj_set_y(o, v);
    internal static void SetSize(IntPtr o, int w, int h) => lv_obj_set_size(o, w, h);
    internal static void SetWidth(IntPtr o, int v) => lv_obj_set_width(o, v);
    internal static void SetHeight(IntPtr o, int v) => lv_obj_set_height(o, v);
    internal static void Align(IntPtr o, int a, int x, int y) => lv_obj_align(o, a, x, y);
    internal static void AlignTo(IntPtr o, IntPtr b, int a, int x, int y) => lv_obj_align_to(o, b, a, x, y);
    internal static void UpdateLayout(IntPtr o) => lv_obj_update_layout(o);
    internal static int GetX(IntPtr o) => lv_obj_get_x(o);
    internal static int GetY(IntPtr o) => lv_obj_get_y(o);
    internal static int GetWidth(IntPtr o) => lv_obj_get_width(o);
    internal static int GetHeight(IntPtr o) => lv_obj_get_height(o);
    internal static IntPtr GetParent(IntPtr o) => lv_obj_get_parent(o);
    internal static void SetStylePadLeft(IntPtr o, int v, uint s) => lv_obj_set_style_pad_left(o, v, s);
    internal static void SetStylePadRight(IntPtr o, int v, uint s) => lv_obj_set_style_pad_right(o, v, s);
    internal static void SetStylePadTop(IntPtr o, int v, uint s) => lv_obj_set_style_pad_top(o, v, s);
    internal static void SetStylePadBottom(IntPtr o, int v, uint s) => lv_obj_set_style_pad_bottom(o, v, s);
    internal static void SetStylePadAll(IntPtr o, int v, uint s)
    {
        SetStylePadLeft(o, v, s);
        SetStylePadRight(o, v, s);
        SetStylePadTop(o, v, s);
        SetStylePadBottom(o, v, s);
    }
    internal static void AddFlag(IntPtr o, uint f) => lv_obj_add_flag(o, f);
    internal static void ClearFlag(IntPtr o, uint f) => lv_obj_clear_flag(o, f);
    internal static bool HasFlag(IntPtr o, uint f) => lv_obj_has_flag(o, f) != 0;
    internal static void AddState(IntPtr o, uint s) => lv_obj_add_state(o, s);
    internal static void ClearState(IntPtr o, uint s) => lv_obj_clear_state(o, s);
    internal static bool HasState(IntPtr o, uint s) => lv_obj_has_state(o, s) != 0;
    internal static void SetChecked(IntPtr o, byte v) { if (v != 0) AddState(o, LV_STATE_CHECKED); else ClearState(o, LV_STATE_CHECKED); }
    internal static void SetExtClickArea(IntPtr o, int v) => lv_obj_set_ext_click_area(o, v);
    internal static void ScrollTo(IntPtr o, int x, int y, int a) => lv_obj_scroll_to(o, x, y, a);
    internal static void ScrollBy(IntPtr o, int x, int y, int a) => lv_obj_scroll_by(o, x, y, a);
    internal static void Invalidate(IntPtr o) => lv_obj_invalidate(o);

    internal static void SetLabelLongMode(IntPtr o, byte v) => lv_label_set_long_mode(o, v);
    internal static void SetLabelRecolor(IntPtr o, byte v) => lv_label_set_recolor(o, v);
    internal static void SetLabelText(IntPtr o, byte* t) => lv_label_set_text(o, (sbyte*)t);
    internal static void SetLabelText(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetLabelText(o, p); }
    internal static void SetCheckboxText(IntPtr o, byte* t) => lv_checkbox_set_text(o, (sbyte*)t);
    internal static void SetCheckboxText(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetCheckboxText(o, p); }
    internal static void SetTextAreaText(IntPtr o, byte* t) => lv_textarea_set_text(o, (sbyte*)t);
    internal static void SetTextAreaText(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetTextAreaText(o, p); }
    internal static void AddTextAreaText(IntPtr o, byte* t) => lv_textarea_add_text(o, (sbyte*)t);
    internal static void AddTextAreaText(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) AddTextAreaText(o, p); }
    internal static void DeleteTextAreaCharacter(IntPtr o) => lv_textarea_del_char(o);
    internal static void SetTextAreaPlaceholderText(IntPtr o, byte* t) => lv_textarea_set_placeholder_text(o, (sbyte*)t);
    internal static void SetTextAreaPlaceholderText(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetTextAreaPlaceholderText(o, p); }
    internal static void SetTextAreaOneLine(IntPtr o, byte v) => lv_textarea_set_one_line(o, v);
    internal static void SetTextAreaPasswordMode(IntPtr o, byte v) => lv_textarea_set_password_mode(o, v);
    internal static void SetTextAreaMaxLength(IntPtr o, uint v) => lv_textarea_set_max_length(o, v);
    internal static uint GetTextAreaCursorPosition(IntPtr o) => lv_textarea_get_cursor_pos(o);
    internal static void SetTextAreaCursorPosition(IntPtr o, int v) => lv_textarea_set_cursor_pos(o, v);

    internal static void SetBarRange(IntPtr o, int min, int max) => lv_bar_set_range(o, min, max);
    internal static void SetBarValue(IntPtr o, int v, int a) => lv_bar_set_value(o, v, a);
    internal static void SetBarStartValue(IntPtr o, int v, int a) => lv_bar_set_start_value(o, v, a);
    internal static int GetBarValue(IntPtr o) => lv_bar_get_value(o);
    internal static int GetBarStartValue(IntPtr o) => lv_bar_get_start_value(o);
    internal static int GetBarMinimum(IntPtr o) => lv_bar_get_min_value(o);
    internal static int GetBarMaximum(IntPtr o) => lv_bar_get_max_value(o);
    internal static void SetBarMode(IntPtr o, byte v) => lv_bar_set_mode(o, v);
    internal static bool IsSliderDragged(IntPtr o) => lv_slider_is_dragged(o) != 0;
    internal static void SetArcAngles(IntPtr o, ushort s, ushort e) => lv_arc_set_angles(o, s, e);
    internal static void SetArcBackgroundAngles(IntPtr o, ushort s, ushort e) => lv_arc_set_bg_angles(o, s, e);
    internal static void SetArcRotation(IntPtr o, ushort v) => lv_arc_set_rotation(o, v);
    internal static void SetArcRange(IntPtr o, short min, short max) => lv_arc_set_range(o, min, max);
    internal static void SetArcValue(IntPtr o, short v) => lv_arc_set_value(o, v);
    internal static short GetArcValue(IntPtr o) => lv_arc_get_value(o);

    internal static void SetDropdownOptions(IntPtr o, byte* t) => lv_dropdown_set_options(o, (sbyte*)t);
    internal static void SetDropdownOptions(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetDropdownOptions(o, p); }
    internal static void SetDropdownText(IntPtr o, byte* t) => lv_dropdown_set_text(o, (sbyte*)t);
    internal static void SetDropdownText(IntPtr o, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetDropdownText(o, p); }
    internal static void SetDropdownSelected(IntPtr o, ushort v) => lv_dropdown_set_selected(o, v);
    internal static ushort GetDropdownSelected(IntPtr o) => lv_dropdown_get_selected(o);
    internal static ushort GetDropdownOptionCount(IntPtr o) => lv_dropdown_get_option_cnt(o);
    internal static void OpenDropdown(IntPtr o) => lv_dropdown_open(o);
    internal static void CloseDropdown(IntPtr o) => lv_dropdown_close(o);
    internal static bool IsDropdownOpen(IntPtr o) => lv_dropdown_is_open(o) != 0;
    internal static void SetRollerOptions(IntPtr o, byte* t, byte m) => lv_roller_set_options(o, (sbyte*)t, m);
    internal static void SetRollerOptions(IntPtr o, ReadOnlySpan<byte> t, byte m) { fixed (byte* p = t) SetRollerOptions(o, p, m); }
    internal static void SetRollerSelected(IntPtr o, ushort v, int a) => lv_roller_set_selected(o, v, a);
    internal static ushort GetRollerSelected(IntPtr o) => lv_roller_get_selected(o);
    internal static void SetRollerVisibleRowCount(IntPtr o, byte v) => lv_roller_set_visible_row_count(o, v);
    internal static void SetTableCellValue(IntPtr o, ushort r, ushort c, byte* t) => lv_table_set_cell_value(o, r, c, (sbyte*)t);
    internal static void SetTableCellValue(IntPtr o, ushort r, ushort c, ReadOnlySpan<byte> t) { fixed (byte* p = t) SetTableCellValue(o, r, c, p); }
    internal static void SetTableRowCount(IntPtr o, ushort v) => lv_table_set_row_cnt(o, v);
    internal static void SetTableColumnCount(IntPtr o, ushort v) => lv_table_set_col_cnt(o, v);
    internal static void SetTableColumnWidth(IntPtr o, ushort c, int w) => lv_table_set_col_width(o, c, w);
    internal static ushort GetTableRowCount(IntPtr o) => lv_table_get_row_cnt(o);
    internal static ushort GetTableColumnCount(IntPtr o) => lv_table_get_col_cnt(o);

    internal static void AddEventCallback(IntPtr o, delegate* unmanaged<IntPtr, void> cb, uint f, IntPtr d) => lv_obj_add_event_cb(o, cb, f, d);
    internal static uint GetEventCode(IntPtr e) => lv_event_get_code(e);
    internal static IntPtr GetEventTarget(IntPtr e) => lv_event_get_target(e);
    internal static IntPtr GetEventCurrentTarget(IntPtr e) => lv_event_get_current_target(e);
    internal static IntPtr GetEventParameter(IntPtr e) => lv_event_get_param(e);
    internal static IntPtr GetEventUserData(IntPtr e) => lv_event_get_user_data(e);
    internal static uint GetEventKey(IntPtr e) => lv_event_get_key(e);
    internal static void StopEventBubbling(IntPtr e) => lv_event_stop_bubbling(e);
    internal static void StopEventProcessing(IntPtr e) => lv_event_stop_processing(e);

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
    [DllImport("*", EntryPoint = "lv_obj_add_event_cb")] private static extern IntPtr lv_obj_add_event_cb(IntPtr o, delegate* unmanaged<IntPtr, void> cb, uint f, IntPtr d);
    [DllImport("*", EntryPoint = "lv_event_get_code")] private static extern uint lv_event_get_code(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_target")] private static extern IntPtr lv_event_get_target(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_current_target")] private static extern IntPtr lv_event_get_current_target(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_param")] private static extern IntPtr lv_event_get_param(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_user_data")] private static extern IntPtr lv_event_get_user_data(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_get_key")] private static extern uint lv_event_get_key(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_stop_bubbling")] private static extern void lv_event_stop_bubbling(IntPtr e);
    [DllImport("*", EntryPoint = "lv_event_stop_processing")] private static extern void lv_event_stop_processing(IntPtr e);
}
