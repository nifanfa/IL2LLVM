#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <time.h>

#ifdef _WIN32
#include <windows.h>
#endif

long long GetCurrentTimeMilliseconds(void)
{
#ifdef _WIN32
    FILETIME fileTime;
    GetSystemTimeAsFileTime(&fileTime);
    uint64_t ticks = ((uint64_t)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
    return (long long)(ticks / 10000ULL - 11644473600000ULL);
#else
    struct timespec time;
    if (timespec_get(&time, TIME_UTC) == 0)
        return 0;
    return (long long)time.tv_sec * 1000LL + time.tv_nsec / 1000000L;
#endif
}

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

static void WriteCodePoint(uint32_t value)
{
#ifdef _WIN32
    static int consoleChecked;
    static HANDLE consoleHandle;
    if (!consoleChecked)
    {
        DWORD mode;
        HANDLE handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (GetConsoleMode(handle, &mode))
            consoleHandle = handle;
        consoleChecked = 1;
    }
    if (consoleHandle)
    {
        wchar_t characters[2];
        DWORD length = 1;
        if (value > 0xffff)
        {
            value -= 0x10000;
            characters[0] = (wchar_t)(0xd800 | (value >> 10));
            characters[1] = (wchar_t)(0xdc00 | (value & 0x3ff));
            length = 2;
        }
        else
            characters[0] = (wchar_t)value;
        WriteConsoleW(consoleHandle, characters, length, NULL, NULL);
        return;
    }
#endif
    WriteUtf8(value);
}

void System_Console_Write_Char(uint16_t value)
{
    static uint16_t pendingHighSurrogate;

    if (pendingHighSurrogate)
    {
        if (value >= 0xdc00 && value <= 0xdfff)
        {
            WriteCodePoint(0x10000 + ((pendingHighSurrogate - 0xd800) << 10) + (value - 0xdc00));
            pendingHighSurrogate = 0;
            return;
        }
        WriteCodePoint(0xfffd);
        pendingHighSurrogate = 0;
    }
    if (value >= 0xd800 && value <= 0xdbff)
        pendingHighSurrogate = value;
    else
        WriteCodePoint(value >= 0xdc00 && value <= 0xdfff ? 0xfffd : value);
}
