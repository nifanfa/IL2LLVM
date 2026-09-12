#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

#ifdef _WIN32
#include <wchar.h>
#endif

void System_Console_Write_System_ByReference_System_Byte(const char* str)
{
    if (str == NULL)
        return;
    printf("%s", str);
}

void System_Console_WriteLine_System_ByReference_System_Byte(const char* str)
{
    System_Console_Write_System_ByReference_System_Byte(str);
    printf("\n");
}

#ifdef _WIN32

void System_Console_Write_System_ByReference_System_Char(const wchar_t* str)
{
    if (str == NULL)
        return;
    wprintf(L"%ls", str);
}

void System_Console_WriteLine_System_ByReference_System_Char(const wchar_t* str)
{
    System_Console_Write_System_ByReference_System_Char(str);
    wprintf(L"\n");
}

#else

static void WriteUtf8(uint32_t value)
{
    if (value <= 0x7f)
    {
        fputc((int)value, stdout);
    }
    else if (value <= 0x7ff)
    {
        fputc((int)(0xc0 | (value >> 6)), stdout);
        fputc((int)(0x80 | (value & 0x3f)), stdout);
    }
    else if (value <= 0xffff)
    {
        fputc((int)(0xe0 | (value >> 12)), stdout);
        fputc((int)(0x80 | ((value >> 6) & 0x3f)), stdout);
        fputc((int)(0x80 | (value & 0x3f)), stdout);
    }
    else
    {
        fputc((int)(0xf0 | (value >> 18)), stdout);
        fputc((int)(0x80 | ((value >> 12) & 0x3f)), stdout);
        fputc((int)(0x80 | ((value >> 6) & 0x3f)), stdout);
        fputc((int)(0x80 | (value & 0x3f)), stdout);
    }
}

void System_Console_Write_System_ByReference_System_Char(const uint16_t* str)
{
    if (str == NULL)
        return;

    while (*str != 0)
    {
        uint32_t value = *str++;
        if (value >= 0xd800 && value <= 0xdbff)
        {
            uint32_t low = *str;
            if (low >= 0xdc00 && low <= 0xdfff)
            {
                str++;
                value = 0x10000 + ((value - 0xd800) << 10) + (low - 0xdc00);
            }
            else
            {
                value = 0xfffd;
            }
        }
        else if (value >= 0xdc00 && value <= 0xdfff)
        {
            value = 0xfffd;
        }
        WriteUtf8(value);
    }
}

void System_Console_WriteLine_System_ByReference_System_Char(const uint16_t* str)
{
    System_Console_Write_System_ByReference_System_Char(str);
    fputc('\n', stdout);
}

#endif

void System_Console_WriteLine_Int32(int value)
{
    printf("%d\n", value);
}

void System_Console_WriteLine_IntPtr(size_t value)
{
    printf("%zu\n", value);
}

void Enter(void* obj)
{
    (void)obj;
}

void Exit(void* obj)
{
    (void)obj;
}
