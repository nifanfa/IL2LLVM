#include <Arduino.h>

#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <sys/time.h>

extern "C" {

int64_t GetCurrentTimeMilliseconds(void)
{
    struct timeval now;
    if (gettimeofday(&now, nullptr) == 0)
        return (int64_t)now.tv_sec * 1000 + now.tv_usec / 1000;

    return (int64_t)millis();
}

static void WriteUtf8(uint32_t value)
{
    unsigned char bytes[4];
    size_t length;

    if (value <= 0x7f)
    {
        bytes[0] = (unsigned char)value;
        length = 1;
    }
    else if (value <= 0x7ff)
    {
        bytes[0] = (unsigned char)(0xc0 | (value >> 6));
        bytes[1] = (unsigned char)(0x80 | (value & 0x3f));
        length = 2;
    }
    else if (value <= 0xffff)
    {
        bytes[0] = (unsigned char)(0xe0 | (value >> 12));
        bytes[1] = (unsigned char)(0x80 | ((value >> 6) & 0x3f));
        bytes[2] = (unsigned char)(0x80 | (value & 0x3f));
        length = 3;
    }
    else
    {
        bytes[0] = (unsigned char)(0xf0 | (value >> 18));
        bytes[1] = (unsigned char)(0x80 | ((value >> 12) & 0x3f));
        bytes[2] = (unsigned char)(0x80 | ((value >> 6) & 0x3f));
        bytes[3] = (unsigned char)(0x80 | (value & 0x3f));
        length = 4;
    }

    fwrite(bytes, 1, length, stdout);
}

void System_Console_Write_Char(uint16_t value)
{
    static uint16_t pendingHighSurrogate;
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

}
