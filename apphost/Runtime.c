#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>

typedef struct GCFrame GCFrame;
typedef struct ExceptionFrame ExceptionFrame;
typedef struct JumpBuffer JumpBuffer;
typedef struct StackPointer StackPointer;

#pragma warning(push)
#pragma warning(disable: 4028 4273 4392)
extern void* memcpy(void* destination, const void* source, size_t count);
extern void* memset(void* destination, int value, size_t count);
extern void* calloc(size_t count, size_t size);
extern void free(void* value);
extern void abort(void);
extern int setjmp(JumpBuffer* buffer, StackPointer* stackPointer);
extern void longjmp(JumpBuffer* buffer, int value);
#pragma warning(pop)

extern void PushGCFrame(GCFrame* frame, void* roots, int rootCount);
extern void PopGCFrame(GCFrame* frame);
extern GCFrame* GetTopGCFrame(void);
extern void UnwindGCFrames(GCFrame* frame);
extern void PushExceptionFrame(ExceptionFrame* frame, void* buffer);
extern void PopExceptionFrame(ExceptionFrame* frame);
extern ExceptionFrame* GetTopExceptionFrame(void);

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

void Enter(void* obj)
{
    (void)obj;
}

void Exit(void* obj)
{
    (void)obj;
}

struct GCFrame
{
    GCFrame* Previous;
    void* Roots;
    int RootCount;
};

struct ExceptionFrame
{
    ExceptionFrame* Previous;
    void* Buffer;
    GCFrame* GCFrame;
};

static GCFrame* topGCFrame;
static ExceptionFrame* topExceptionFrame;

void PushGCFrame(GCFrame* frame, void* roots, int rootCount)
{
    frame->Previous = topGCFrame;
    frame->Roots = roots;
    frame->RootCount = rootCount;
    topGCFrame = frame;
}

void PopGCFrame(GCFrame* frame)
{
    topGCFrame = frame->Previous;
}

GCFrame* GetTopGCFrame(void)
{
    return topGCFrame;
}

void UnwindGCFrames(GCFrame* frame)
{
    topGCFrame = frame;
}

void PushExceptionFrame(ExceptionFrame* frame, void* buffer)
{
    frame->Previous = topExceptionFrame;
    frame->Buffer = buffer;
    frame->GCFrame = topGCFrame;
    topExceptionFrame = frame;
}

void PopExceptionFrame(ExceptionFrame* frame)
{
    if (topExceptionFrame == frame)
        topExceptionFrame = frame->Previous;
}

ExceptionFrame* GetTopExceptionFrame(void)
{
    return topExceptionFrame;
}
