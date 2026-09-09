#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>

void System_Console_Write_String(const unsigned short* str)
{
    if (str == NULL)
        return;

    while (*str != 0)
    {
        if (putwchar((wchar_t)*str++) == WEOF)
            return;
    }
}

void System_Console_WriteLine_String(const unsigned short* str)
{
    System_Console_Write_String(str);
    putwchar(L'\n');
}

void System_Console_Write_System_ReadOnlySpan_1_System_Byte(const char* value)
{
    if (value != NULL)
        fputs(value, stdout);
}

void System_Console_WriteLine_System_ReadOnlySpan_1_System_Byte(const char* value)
{
    System_Console_Write_System_ReadOnlySpan_1_System_Byte(value);
    fputc('\n', stdout);
}

void System_Console_WriteLine_Int32(int value)
{
    printf("%d\n", value);
}

void System_Console_WriteLine_IntPtr(size_t value)
{
    printf("%zu\n", value);
}

void System_Threading_Monitor_Enter_Object(void* obj __attribute__((unused)))
{
}

void System_Threading_Monitor_Exit_Object(void* obj __attribute__((unused)))
{
}
