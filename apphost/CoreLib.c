#include <stdio.h>
#include <wchar.h>

void System_Console_Write_String(const wchar_t* str)
{
    if (str == NULL)
        return;
    wprintf(L"%ls", str);
}

void System_Console_WriteLine_String(const wchar_t* str)
{
    System_Console_Write_String(str);
    wprintf(L"\n");
}

void System_Console_WriteLine_Int32(int value)
{
    printf("%d\n", value);
}

void System_Console_WriteLine_IntPtr(size_t value)
{
    printf("%zu\n", value);
}
