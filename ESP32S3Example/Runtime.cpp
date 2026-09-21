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

void System_Console_Write_System_ByReference_System_Byte(const char* value)
{
    if (value != nullptr)
        fputs(value, stdout);
}

void System_Console_WriteLine_System_ByReference_System_Byte(const char* value)
{
    System_Console_Write_System_ByReference_System_Byte(value);
    fputc('\n', stdout);
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

void System_Console_Write_System_ByReference_System_Char(const uint16_t* value)
{
    if (value == nullptr)
        return;

    while (*value != 0)
    {
        uint32_t codePoint = *value++;
        if (codePoint >= 0xd800 && codePoint <= 0xdbff)
        {
            uint32_t low = *value;
            if (low >= 0xdc00 && low <= 0xdfff)
            {
                ++value;
                codePoint = 0x10000 + ((codePoint - 0xd800) << 10) + (low - 0xdc00);
            }
            else
            {
                codePoint = 0xfffd;
            }
        }
        else if (codePoint >= 0xdc00 && codePoint <= 0xdfff)
        {
            codePoint = 0xfffd;
        }

        WriteUtf8(codePoint);
    }
}

void System_Console_WriteLine_System_ByReference_System_Char(const uint16_t* value)
{
    System_Console_Write_System_ByReference_System_Char(value);
    fputc('\n', stdout);
}

}
