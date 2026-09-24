using System;
using System.Runtime;
using System.Runtime.InteropServices;

public static unsafe partial class Arduino
{
    // This increase stack size of Arduino loop task to avoid stack overflow in some cases.
    [RuntimeExport("_Z27getArduinoLoopTaskStackSizev")]
    private static uint GetArduinoLoopTaskStackSize() => 16 * 1024;

    // ESP32-S3 Arduino variant pin mapping.
    public const byte NUM_DIGITAL_PINS = 49;
    public const byte LED_BUILTIN = 49;
    public const byte SS = 10;
    public const byte MOSI = 11;
    public const byte MISO = 13;
    public const byte SCK = 12;
    public const byte SDA = 8;
    public const byte SCL = 9;
    [DllImport("*", EntryPoint = "analogWriteFrequency")]
    public static extern void analogWriteFrequency(byte pin, uint frequency);
    [DllImport("*", EntryPoint = "analogWriteResolution")]
    public static extern void analogWriteResolution(byte pin, byte bits);
    [DllImport("*", EntryPoint = "temperatureRead")]
    public static extern float temperatureRead();
    [DllImport("*", EntryPoint = "init")]
    public static extern void init();
    [DllImport("*", EntryPoint = "initVariant")]
    public static extern void initVariant();
    [DllImport("*", EntryPoint = "initArduino")]
    public static extern void initArduino();
    [DllImport("*", EntryPoint = "getLocalTime")]
    public static extern bool getLocalTime(IntPtr info, uint milliseconds = 5000U);
    [DllImport("*", EntryPoint = "configTime")]
    public static extern void configTime(int gmtOffsetSeconds, int daylightOffsetSeconds,
        sbyte* server1, sbyte* server2, sbyte* server3);
    [DllImport("*", EntryPoint = "configTzTime")]
    public static extern void configTzTime(sbyte* timeZone, sbyte* server1, sbyte* server2, sbyte* server3);
    [DllImport("*", EntryPoint = "attachInterruptArg")]
    public static extern void attachInterruptArg(byte pin, delegate* unmanaged<void*, void> callback,
        void* argument, int mode);
    [DllImport("*", EntryPoint = "enableInterrupt")]
    public static extern void enableInterrupt(byte pin);
    [DllImport("*", EntryPoint = "disableInterrupt")]
    public static extern void disableInterrupt(byte pin);
    [DllImport("*", EntryPoint = "digitalPinToTouchChannel")]
    public static extern sbyte digitalPinToTouchChannel(byte pin);
    [DllImport("*", EntryPoint = "digitalPinToAnalogChannel")]
    public static extern sbyte digitalPinToAnalogChannel(byte pin);
    [DllImport("*", EntryPoint = "analogChannelToDigitalPin")]
    public static extern sbyte analogChannelToDigitalPin(byte channel);

    public const double PI = 3.1415926535897932384626433832795;
    public const double HALF_PI = 1.5707963267948966192313216916398;
    public const double TWO_PI = 6.283185307179586476925286766559;
    public const double DEG_TO_RAD = 0.017453292519943295769236907684886;
    public const double RAD_TO_DEG = 57.295779513082320876798154814105;
    public const double EULER = 2.718281828459045235360287471352;

    public const byte SERIAL = 0;
    public const byte DISPLAY = 1;
    public const byte LSBFIRST = 0;
    public const byte MSBFIRST = 1;
    public const int RISING = 1;
    public const int FALLING = 2;
    public const int CHANGE = 3;
    public const int ONLOW = 4;
    public const int ONHIGH = 5;
    public const int ONLOW_WE = 12;
    public const int ONHIGH_WE = 13;
    public const int DEFAULT = 1;
    public const int EXTERNAL = 0;

    public const byte LOW = 0;
    public const byte HIGH = 1;
    public const byte INPUT = 1;
    public const byte OUTPUT = 3;
    public const byte INPUT_PULLUP = 5;
    public const byte INPUT_PULLDOWN = 9;
    public const byte OUTPUT_OPEN_DRAIN = 0x13;
    public const byte ANALOG = 0xC0;
    public const sbyte NOT_A_PIN = -1;
    public const sbyte NOT_A_PORT = -1;
    public const sbyte NOT_AN_INTERRUPT = -1;
    public const byte NOT_ON_TIMER = 0;

    public static byte lowByte(ushort value) => (byte)value;
    public static byte highByte(ushort value) => (byte)(value >> 8);
    public static uint bit(byte value) => 1U << value;
    public static uint bitRead(uint value, byte bitIndex) => (value >> bitIndex) & 1U;
    public static void bitSet(ref uint value, byte bitIndex) => value |= 1U << bitIndex;
    public static void bitClear(ref uint value, byte bitIndex) => value &= ~(1U << bitIndex);
    public static void bitToggle(ref uint value, byte bitIndex) => value ^= 1U << bitIndex;
    public static void bitWrite(ref uint value, byte bitIndex, uint bitValue)
    {
        if (bitValue != 0) bitSet(ref value, bitIndex); else bitClear(ref value, bitIndex);
    }

    private static uint randomState = 0x6D2B79F5;
    public static void randomSeed(uint seed) => randomState = seed == 0 ? 0x6D2B79F5U : seed;
    public static void useRealRandomGenerator(bool useHardwareRandom) { }
    public static int random(int max)
    {
        if (max <= 0) return 0;
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return (int)(randomState % (uint)max);
    }
    public static int random(int min, int max) => max <= min ? min : min + random(max - min);
    public static int map(int value, int fromLow, int fromHigh, int toLow, int toHigh) =>
        (int)((long)(value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow) + toLow);
    public static ushort makeWord(ushort value) => value;
    public static ushort makeWord(byte high, byte low) => (ushort)((high << 8) | low);

    [DllImport("*", EntryPoint = "pinMode")]
    public static extern void pinMode(byte pin, byte mode);
    [DllImport("*", EntryPoint = "digitalWrite")]
    public static extern void digitalWrite(byte pin, byte value);
    [DllImport("*", EntryPoint = "digitalRead")]
    public static extern int digitalRead(byte pin);
    [DllImport("*", EntryPoint = "analogWrite")]
    public static extern void analogWrite(byte pin, int value);
    [DllImport("*", EntryPoint = "micros")]
    public static extern uint micros();
    [DllImport("*", EntryPoint = "millis")]
    public static extern uint millis();
    [DllImport("*", EntryPoint = "delay")]
    public static extern void delay(uint milliseconds);
    [DllImport("*", EntryPoint = "delayMicroseconds")]
    public static extern void delayMicroseconds(uint microseconds);
    [DllImport("*", EntryPoint = "yield")]
    public static extern void yield();
    [DllImport("*", EntryPoint = "pulseIn")]
    public static extern uint pulseIn(byte pin, byte state, uint timeout = 1000000U);
    [DllImport("*", EntryPoint = "pulseInLong")]
    public static extern uint pulseInLong(byte pin, byte state, uint timeout = 1000000U);
    [DllImport("*", EntryPoint = "shiftIn")]
    public static extern byte shiftIn(byte dataPin, byte clockPin, byte bitOrder);
    [DllImport("*", EntryPoint = "shiftOut")]
    public static extern void shiftOut(byte dataPin, byte clockPin, byte bitOrder, byte value);
    [DllImport("*", EntryPoint = "attachInterrupt")]
    public static extern void attachInterrupt(byte pin, delegate* unmanaged<void> callback, int mode);
    [DllImport("*", EntryPoint = "detachInterrupt")]
    public static extern void detachInterrupt(byte pin);
}
