#include <stdio.h>
#include <wchar.h>

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

void System_Console_WriteLine_Int32(int value)
{
    printf("%d\n", value);
}

void System_Console_WriteLine_IntPtr(size_t value)
{
    printf("%zu\n", value);
}

void System_Threading_Monitor_Enter_Object(void* obj)
{
    // TO-DO
}

void System_Threading_Monitor_Exit_Object(void* obj)
{
	// TO-DO
}
