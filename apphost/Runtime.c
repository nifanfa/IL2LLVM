#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

#ifdef _WIN32
#include <wchar.h>
#endif

void System_Console_Write_System_ByReference_System_Byte(const char* str) { if (str != NULL) printf("%s", str); }
void System_Console_WriteLine_System_ByReference_System_Byte(const char* str) { printf("%s\n", str == NULL ? "" : str); }

#ifdef _WIN32

void System_Console_Write_System_ByReference_System_Char(const wchar_t* str) { if (str != NULL) wprintf(L"%ls", str); }
void System_Console_WriteLine_System_ByReference_System_Char(const wchar_t* str) { wprintf(L"%ls\n", str == NULL ? L"" : str); }

#else

static void WriteUtf8(uint32_t value)
{
    unsigned char bytes[4];
    size_t length = value <= 0x7f ? 1 : value <= 0x7ff ? 2 : value <= 0xffff ? 3 : 4;
    if (length == 1)
        bytes[0] = (unsigned char)value;
    else
    {
        for (size_t index = length; --index != 0; value >>= 6)
            bytes[index] = (unsigned char)(0x80 | (value & 0x3f));
        bytes[0] = (unsigned char)((0xf00 >> length) | value);
    }
    fwrite(bytes, 1, length, stdout);
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

void System_Console_WriteLine_Int32(int value) { printf("%d\n", value); }
void System_Console_WriteLine_IntPtr(size_t value) { printf("%zu\n", value); }
void Enter(void* value) { (void)value; }
void Exit(void* value) { (void)value; }
