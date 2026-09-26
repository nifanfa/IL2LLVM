#include <linux/kernel.h>
#include <linux/ktime.h>
#include <linux/types.h>
#include <linux/string.h>
#include <linux/vmalloc.h>

long long GetCurrentTimeMilliseconds(void)
{
    struct timespec64 time;
    ktime_get_real_ts64(&time);
    return (long long)time.tv_sec * 1000LL + time.tv_nsec / 1000000L;
}

static char consoleBuffer[256];
static size_t consoleLength;
static uint16_t pendingHighSurrogate;

static void WriteUtf8(uint32_t value)
{
    char bytes[4];
    size_t length;
    if (value < 0x80)
    {
        bytes[0] = value;
        length = 1;
    }
    else if (value < 0x800)
    {
        bytes[0] = 0xc0 | (value >> 6);
        bytes[1] = 0x80 | (value & 0x3f);
        length = 2;
    }
    else if (value < 0x10000)
    {
        bytes[0] = 0xe0 | (value >> 12);
        bytes[1] = 0x80 | ((value >> 6) & 0x3f);
        bytes[2] = 0x80 | (value & 0x3f);
        length = 3;
    }
    else
    {
        bytes[0] = 0xf0 | (value >> 18);
        bytes[1] = 0x80 | ((value >> 12) & 0x3f);
        bytes[2] = 0x80 | ((value >> 6) & 0x3f);
        bytes[3] = 0x80 | (value & 0x3f);
        length = 4;
    }

    if (consoleLength + length >= sizeof(consoleBuffer))
    {
        consoleBuffer[consoleLength] = 0;
        printk(KERN_INFO "%s", consoleBuffer);
        consoleLength = 0;
    }
    memcpy(consoleBuffer + consoleLength, bytes, length);
    consoleLength += length;
    if (value == '\n')
    {
        consoleBuffer[consoleLength] = 0;
        printk(KERN_INFO "%s", consoleBuffer);
        consoleLength = 0;
    }
}

void System_Console_Write_Char(uint16_t value)
{
    if (pendingHighSurrogate)
    {
        if (value >= 0xdc00 && value <= 0xdfff)
        {
            WriteUtf8(0x10000 + ((pendingHighSurrogate - 0xd800) << 10) + (value - 0xdc00));
            pendingHighSurrogate = 0;
            return;
        }
        WriteUtf8(0xfffd);
        pendingHighSurrogate = 0;
    }
    if (value >= 0xd800 && value <= 0xdbff)
        pendingHighSurrogate = value;
    else
        WriteUtf8(value >= 0xdc00 && value <= 0xdfff ? 0xfffd : value);
}
void* malloc(size_t size) { return vmalloc(size == 0 ? 1 : size); }

void free(void* value) { vfree(value); }
void abort(void) { panic("Managed runtime aborted"); }
