#include <linux/kernel.h>
#include <linux/nls.h>
#include <linux/string.h>
#include <linux/vmalloc.h>

extern void* memcpy(void* destination, const void* source, size_t count);
extern void* memset(void* destination, int value, size_t count);
extern int setjmp(void* buffer, void* stack_pointer);
extern void longjmp(void* buffer, int value);

static void write_utf16(const unsigned short* value)
{
    char buffer[256];
    int source_length = 0;
    int length;

    if (value == NULL)
        return;
    while (value[source_length] != 0 && source_length < (int)(sizeof(buffer) / 2))
        source_length++;
    length = utf16s_to_utf8s(value, source_length, UTF16_HOST_ENDIAN, buffer, sizeof(buffer) - 1);
    if (length < 0)
        return;
    buffer[length] = 0;
    printk(KERN_INFO "%s", buffer);
}

void System_Console_Write_System_ByReference_System_Byte(const char* value)
{
    if (value != NULL)
        printk(KERN_INFO "%s", value);
}

void System_Console_WriteLine_System_ByReference_System_Byte(const char* value)
{
    System_Console_Write_System_ByReference_System_Byte(value);
    printk(KERN_CONT "\n");
}

void System_Console_Write_System_ByReference_System_Char(const unsigned short* value)
{
    write_utf16(value);
}

void System_Console_WriteLine_System_ByReference_System_Char(const unsigned short* value)
{
    write_utf16(value);
    printk(KERN_CONT "\n");
}

void System_Console_WriteLine_Int32(int value)
{
    printk(KERN_INFO "%d\n", value);
}

void System_Console_WriteLine_IntPtr(size_t value)
{
    printk(KERN_INFO "%zu\n", value);
}

void Enter(void* value)
{
    (void)value;
}

void Exit(void* value)
{
    (void)value;
}

void* malloc(size_t size)
{
    if (size == 0)
        size = 1;
    return vmalloc(size);
}

void* calloc(size_t count, size_t size)
{
    size_t total;
    void* allocation;

    if (size != 0 && count > (~(size_t)0) / size)
        return NULL;
    total = count * size;
    allocation = malloc(total);
    if (allocation == NULL)
        return NULL;
    memset(allocation, 0, total);
    return allocation;
}

void free(void* value)
{
    vfree(value);
}

void abort(void)
{
    panic("Managed runtime aborted");
}
