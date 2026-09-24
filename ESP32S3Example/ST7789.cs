using System.Runtime.InteropServices;
using static Arduino;
using static SPI;

internal unsafe class ST7789
{
    public const ushort Width = 320;
    public const ushort Height = 240;

    private const sbyte ClockPin = 39;
    private const sbyte MosiPin = 38;
    private const sbyte MisoPin = 40;
    private const byte DataCommandPin = 42;
    private static readonly sbyte ResetPin = -1;
    private const byte ChipSelectPin = 45;
    private const byte BacklightPin = 1;

    private const byte LandscapeMemoryAccessControl = 0x60; // landscape: 320x240
    private const byte SoftwareResetCommand = 0x01;
    private const byte SleepOutCommand = 0x11;
    private const byte MemoryAccessControlCommand = 0x36;
    private const byte PixelFormatCommand = 0x3A;
    private const byte ColumnAddressCommand = 0x2A;
    private const byte RowAddressCommand = 0x2B;
    private const byte MemoryWriteCommand = 0x2C;
    private const byte InversionOnCommand = 0x21;
    private const byte DisplayOnCommand = 0x29;
    private const uint SpiFrequencyHz = 40_000_000;
    private const uint BacklightFrequencyHz = 1000;
    private const byte BacklightResolutionBits = 10;
    private const byte MaxBacklightPercent = 100;
    private const ushort ColumnOffset = 0;
    private const ushort RowOffset = 0;

    private bool _initialized;
    public ST7789() => Initialize();

    /// <summary>Initializes the GPIO, backlight, SPI bus, and ST7789 controller.</summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        pinMode(ChipSelectPin, OUTPUT);
        digitalWrite(ChipSelectPin, HIGH);
        pinMode(DataCommandPin, OUTPUT);
        digitalWrite(DataCommandPin, HIGH);

        if (ResetPin >= 0)
        {
            pinMode((byte)ResetPin, OUTPUT);
            digitalWrite((byte)ResetPin, HIGH);
        }

        InitializeBacklight();
        begin(ClockPin, MisoPin, MosiPin, -1);

        ResetController();
        InitializeController();
        _initialized = true;
    }

    private void InitializeController()
    {
        WriteCommand(SleepOutCommand); // Sleep out
        delay(120);

        WriteCommand(MemoryAccessControlCommand);
        WriteData(LandscapeMemoryAccessControl);

        WriteCommand(PixelFormatCommand);
        WriteData(0x05); // RGB565

        WriteCommand(0xB0);
        WriteData(0x00);
        WriteData(0xE8);

        WriteCommand(0xB2);
        WriteData(0x0C);
        WriteData(0x0C);
        WriteData(0x00);
        WriteData(0x33);
        WriteData(0x33);

        WriteCommand(0xB7);
        WriteData(0x35);
        WriteCommand(0xBB);
        WriteData(0x35);
        WriteCommand(0xC0);
        WriteData(0x2C);
        WriteCommand(0xC2);
        WriteData(0x01);
        WriteCommand(0xC3);
        WriteData(0x13);
        WriteCommand(0xC4);
        WriteData(0x20);
        WriteCommand(0xC6);
        WriteData(0x0F);

        WriteCommand(0xD0);
        WriteData(0xA4);
        WriteData(0xA1);
        WriteCommand(0xD6);
        WriteData(0xA1);

        WriteCommand(0xE0);
        WriteData(0xF0);
        WriteData(0x00);
        WriteData(0x04);
        WriteData(0x04);
        WriteData(0x04);
        WriteData(0x05);
        WriteData(0x29);
        WriteData(0x33);
        WriteData(0x3E);
        WriteData(0x38);
        WriteData(0x12);
        WriteData(0x12);
        WriteData(0x28);
        WriteData(0x30);

        WriteCommand(0xE1);
        WriteData(0xF0);
        WriteData(0x07);
        WriteData(0x0A);
        WriteData(0x0D);
        WriteData(0x0B);
        WriteData(0x07);
        WriteData(0x28);
        WriteData(0x33);
        WriteData(0x3E);
        WriteData(0x36);
        WriteData(0x14);
        WriteData(0x14);
        WriteData(0x29);
        WriteData(0x32);

        WriteCommand(InversionOnCommand); // Display inversion on
        WriteCommand(SleepOutCommand); // Sleep out (kept for compatibility with the original sequence)
        delay(120);
        WriteCommand(DisplayOnCommand); // Display on
    }

    private void ResetController()
    {
        if (ResetPin >= 0)
        {
            byte resetPin = (byte)ResetPin;
            digitalWrite(resetPin, LOW);
            delay(50);
            digitalWrite(resetPin, HIGH);
            delay(120);
        }
        else
        {
            // The board does not wire the reset pin.
            WriteCommand(SoftwareResetCommand); // Software reset
            delay(150);
        }
    }

    private void SetAddressWindow(ushort xStart, ushort yStart, ushort xEnd, ushort yEnd)
    {
        ushort colStart = (ushort)(xStart + ColumnOffset);
        ushort colEnd = (ushort)(xEnd + ColumnOffset);
        ushort rowStart = (ushort)(yStart + RowOffset);
        ushort rowEnd = (ushort)(yEnd + RowOffset);

        WriteCommand(ColumnAddressCommand);
        WriteData((byte)(colStart >> 8));
        WriteData((byte)colStart);
        WriteData((byte)(colEnd >> 8));
        WriteData((byte)colEnd);

        WriteCommand(RowAddressCommand);
        WriteData((byte)(rowStart >> 8));
        WriteData((byte)rowStart);
        WriteData((byte)(rowEnd >> 8));
        WriteData((byte)rowEnd);

        WriteCommand(MemoryWriteCommand);
    }

    /// <summary>Writes row-major RGB565 pixels to an inclusive display rectangle.</summary>
    public void DrawPixels(ushort xStart, ushort yStart, ushort xEnd, ushort yEnd, ushort* pixels)
    {
        if (pixels == null || xEnd < xStart || yEnd < yStart ||
            xEnd >= Width || yEnd >= Height)
            return;

        uint width = (uint)(xEnd - xStart + 1);
        uint height = (uint)(yEnd - yStart + 1);
        uint byteCount = checked(width * height * sizeof(ushort));

        SetAddressWindow(xStart, yStart, xEnd, yEnd);
        WritePixelData((byte*)pixels, byteCount);
    }

    /// <summary>Fills the entire 320x240 panel with one RGB565 color.</summary>
    public void Fill(ushort color)
    {
        SetAddressWindow(0, 0, (ushort)(Width - 1), (ushort)(Height - 1));

        beginTransaction(new SPISettings(SpiFrequencyHz, MSBFIRST, SPI_MODE0));
        digitalWrite(ChipSelectPin, LOW);
        digitalWrite(DataCommandPin, HIGH);

        // 32 RGB565 pixels per chunk; avoid managed allocations while transmitting.
        const uint ChunkSize = 64;
        byte* chunk = stackalloc byte[(int)ChunkSize];
        byte high = (byte)(color >> 8);
        byte low = (byte)color;
        for (uint i = 0; i < ChunkSize; i += 2)
        {
            chunk[i] = high;
            chunk[i + 1] = low;
        }

        uint remaining = (uint)Width * Height * 2;
        while (remaining != 0)
        {
            uint count = remaining < ChunkSize ? remaining : ChunkSize;
            writePixels(chunk, count);
            remaining -= count;
        }

        digitalWrite(ChipSelectPin, HIGH);
        endTransaction();
    }

    private void InitializeBacklight()
    {
        AttachBacklightPwm(BacklightPin, BacklightFrequencyHz, BacklightResolutionBits);
        SetBacklightBrightness(90);
    }

    /// <summary>Sets the backlight brightness from 0 to 100 percent.</summary>
    public void SetBacklightBrightness(byte percent)
    {
        if (percent > MaxBacklightPercent)
            return;

        uint duty = (uint)percent * 10;
        if (duty == 1000)
            duty = 1024;
        WriteBacklightPwm(BacklightPin, duty);
    }

    private void WriteCommand(byte command)
    {
        beginTransaction(new SPISettings(SpiFrequencyHz, MSBFIRST, SPI_MODE0));
        digitalWrite(ChipSelectPin, LOW);
        digitalWrite(DataCommandPin, LOW);
        transfer(command);
        digitalWrite(ChipSelectPin, HIGH);
        endTransaction();
    }

    private void WriteData(byte data)
    {
        beginTransaction(new SPISettings(SpiFrequencyHz, MSBFIRST, SPI_MODE0));
        digitalWrite(ChipSelectPin, LOW);
        digitalWrite(DataCommandPin, HIGH);
        transfer(data);
        digitalWrite(ChipSelectPin, HIGH);
        endTransaction();
    }

    private void WritePixelData(byte* data, uint size)
    {
        beginTransaction(new SPISettings(SpiFrequencyHz, MSBFIRST, SPI_MODE0));
        digitalWrite(ChipSelectPin, LOW);
        digitalWrite(DataCommandPin, HIGH);
        writePixels(data, size);
        digitalWrite(ChipSelectPin, HIGH);
        endTransaction();
    }

    [DllImport("*", EntryPoint = "ledcAttach")]
    private static extern bool AttachBacklightPwm(byte pin, uint freq, byte resolution);

    [DllImport("*", EntryPoint = "ledcWrite")]
    private static extern bool WriteBacklightPwm(byte pin, uint duty);
}
