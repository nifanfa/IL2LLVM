#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>
#include <setjmp.h>

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

void* Newobj(size_t size)
{
    return calloc(1, size);
}

void* Newarr(size_t count, size_t size, size_t baseSize)
{
    return calloc(1, baseSize + (count + 1) * size);
}

static void* runtime_exception_top;
static void* runtime_exception_current;

void RuntimeExceptionPush(void* frame, void* buffer)
{
    void** fields = (void**)frame;
    fields[0] = runtime_exception_top;
    fields[1] = buffer;
    runtime_exception_top = frame;
}

void RuntimeExceptionPop(void* frame)
{
    if (runtime_exception_top == frame)
        runtime_exception_top = *(void**)frame;
}

void* RuntimeExceptionBuffer(void* frame)
{
    return ((void**)frame)[1];
}

void* RuntimeExceptionCurrent(void)
{
    return runtime_exception_current;
}

void RuntimeExceptionThrow(void* exception)
{
    runtime_exception_current = exception;
    if (runtime_exception_top != NULL)
        longjmp(*(jmp_buf*)(((void**)runtime_exception_top)[1]), 1);
    abort();
}
