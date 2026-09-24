using System;
using System.Runtime.InteropServices;

public readonly struct SPISettings
{
    public readonly uint _clock;
    public readonly byte _bitOrder;
    public readonly byte _dataMode;

    public SPISettings()
        : this(1000000, SPI.SPI_MSBFIRST, SPI.SPI_MODE0)
    {
    }

    public SPISettings(uint clock, byte bitOrder, byte dataMode)
    {
        _clock = clock;
        _bitOrder = bitOrder;
        _dataMode = dataMode;
    }

    public uint Clock => _clock;
    public byte BitOrder => _bitOrder;
    public byte DataMode => _dataMode;
}

public static unsafe partial class SPI
{
    public const byte FSPI = 0;
    public const byte HSPI = 1;
    public const byte SPI_MODE0 = 0;
    public const byte SPI_MODE1 = 1;
    public const byte SPI_MODE2 = 2;
    public const byte SPI_MODE3 = 3;
    public const byte SPI_LSBFIRST = 0;
    public const byte SPI_MSBFIRST = 1;

    public const uint SPI_CLOCK_DIV2 = 0x00101001;
    public const uint SPI_CLOCK_DIV4 = 0x00241001;
    public const uint SPI_CLOCK_DIV8 = 0x004C1001;
    public const uint SPI_CLOCK_DIV16 = 0x009C1001;
    public const uint SPI_CLOCK_DIV32 = 0x013C1001;
    public const uint SPI_CLOCK_DIV64 = 0x027C1001;
    public const uint SPI_CLOCK_DIV128 = 0x04FC1001;

    private static IntPtr _spi;
    private static sbyte _ss = -1;
    private static bool _useHwSs;
    private static bool _inTransaction;
    private static uint _div;
    private static uint _freq = 1000000;

    public static void begin(sbyte sck = -1, sbyte miso = -1, sbyte mosi = -1, sbyte ss = -1)
    {
        if (_spi != IntPtr.Zero)
            return;

        if (_div == 0)
            _div = spiFrequencyToClockDiv(_freq);

        _spi = spiStartBus(FSPI, _div, SPI_MODE0, SPI_MSBFIRST);
        if (_spi == IntPtr.Zero)
            return;

        _ss = ss;
        if (sck >= 0 && !spiAttachSCK(_spi, sck) ||
            miso >= 0 && !spiAttachMISO(_spi, miso) ||
            mosi >= 0 && !spiAttachMOSI(_spi, mosi))
            end();
    }

    public static void end()
    {
        if (_spi == IntPtr.Zero)
            return;

        spiDetachSCK(_spi);
        spiDetachMISO(_spi);
        spiDetachMOSI(_spi);
        setHwCs(false);
        spiStopBus(_spi);
        _spi = IntPtr.Zero;
        _inTransaction = false;
    }

    public static void setHwCs(bool use)
    {
        if (_ss < 0 || _spi == IntPtr.Zero)
            return;

        if (use && !_useHwSs)
        {
            spiAttachSS(_spi, 0, _ss);
            spiSSEnable(_spi);
        }
        else if (!use && _useHwSs)
        {
            spiSSDisable(_spi);
            spiDetachSS(_spi);
        }

        _useHwSs = use;
    }

    public static void setBitOrder(byte bitOrder) => spiSetBitOrder(_spi, bitOrder);
    public static void setDataMode(byte dataMode) => spiSetDataMode(_spi, dataMode);

    public static void setFrequency(uint frequency)
    {
        _freq = frequency;
        _div = spiFrequencyToClockDiv(_freq);
        spiSetClockDiv(_spi, _div);
    }

    public static void setClockDivider(uint clockDiv)
    {
        _div = clockDiv;
        spiSetClockDiv(_spi, _div);
    }

    public static uint getClockDivider() => spiGetClockDiv(_spi);

    public static void beginTransaction(SPISettings settings)
    {
        _freq = settings._clock;
        _div = spiFrequencyToClockDiv(_freq);
        spiTransaction(_spi, _div, settings._dataMode, settings._bitOrder);
        _inTransaction = true;
    }

    public static void endTransaction()
    {
        if (_inTransaction)
        {
            _inTransaction = false;
            spiEndTransaction(_spi);
        }
    }

    public static void write(byte data)
    {
        if (_inTransaction) spiWriteByteNL(_spi, data); else spiWriteByte(_spi, data);
    }

    public static byte transfer(byte data) =>
        _inTransaction ? spiTransferByteNL(_spi, data) : spiTransferByte(_spi, data);

    public static void write16(ushort data)
    {
        if (_inTransaction) spiWriteShortNL(_spi, data); else spiWriteWord(_spi, data);
    }

    public static ushort transfer16(ushort data) =>
        _inTransaction ? spiTransferShortNL(_spi, data) : spiTransferWord(_spi, data);

    public static void write32(uint data)
    {
        if (_inTransaction) spiWriteLongNL(_spi, data); else spiWriteLong(_spi, data);
    }

    public static uint transfer32(uint data) =>
        _inTransaction ? spiTransferLongNL(_spi, data) : spiTransferLong(_spi, data);

    public static void transfer(void* data, uint size) => transferBytes((byte*)data, (byte*)data, size);

    public static void transferBytes(byte* data, byte* output, uint size)
    {
        if (_inTransaction) spiTransferBytesNL(_spi, data, output, size); else spiTransferBytes(_spi, data, output, size);
    }

    public static void transferBits(uint data, uint* output, byte bits)
    {
        if (_inTransaction) spiTransferBitsNL(_spi, data, output, bits); else spiTransferBits(_spi, data, output, bits);
    }

    public static void writeBytes(byte* data, uint size)
    {
        if (_inTransaction) { spiWriteNL(_spi, data, size); return; }
        spiSimpleTransaction(_spi); spiWriteNL(_spi, data, size); spiEndTransaction(_spi);
    }

    public static void writePixels(void* data, uint size)
    {
        if (_inTransaction) { spiWritePixelsNL(_spi, data, size); return; }
        spiSimpleTransaction(_spi); spiWritePixelsNL(_spi, data, size); spiEndTransaction(_spi);
    }

    public static void writePattern(byte* data, byte size, uint repeat)
    {
        if (size == 0 || size > 64 || repeat == 0)
            return;

        uint remaining = (uint)size * repeat;
        uint maxBytes = (uint)(64 / size) * size;
        byte* buffer = stackalloc byte[64];
        while (remaining != 0)
        {
            uint bytes = remaining > maxBytes ? maxBytes : remaining;
            for (uint i = 0; i < bytes; i++) buffer[i] = data[i % size];
            writeBytes(buffer, bytes);
            remaining -= bytes;
        }
    }

    public static IntPtr bus() => _spi;
    public static sbyte pinSS() => _ss;

    [DllImport("*", EntryPoint = "spiFrequencyToClockDiv")]
    private static extern uint spiFrequencyToClockDiv(uint frequency);
    [DllImport("*", EntryPoint = "spiStartBus")]
    private static extern IntPtr spiStartBus(byte bus, uint divider, byte mode, byte order);
    [DllImport("*", EntryPoint = "spiStopBus")]
    private static extern void spiStopBus(IntPtr handle);
    [DllImport("*", EntryPoint = "spiAttachSCK")]
    private static extern bool spiAttachSCK(IntPtr handle, sbyte pin);
    [DllImport("*", EntryPoint = "spiAttachMISO")]
    private static extern bool spiAttachMISO(IntPtr handle, sbyte pin);
    [DllImport("*", EntryPoint = "spiAttachMOSI")]
    private static extern bool spiAttachMOSI(IntPtr handle, sbyte pin);
    [DllImport("*", EntryPoint = "spiDetachSCK")]
    private static extern bool spiDetachSCK(IntPtr handle);
    [DllImport("*", EntryPoint = "spiDetachMISO")]
    private static extern bool spiDetachMISO(IntPtr handle);
    [DllImport("*", EntryPoint = "spiDetachMOSI")]
    private static extern bool spiDetachMOSI(IntPtr handle);
    [DllImport("*", EntryPoint = "spiAttachSS")]
    private static extern bool spiAttachSS(IntPtr handle, byte number, sbyte pin);
    [DllImport("*", EntryPoint = "spiDetachSS")]
    private static extern bool spiDetachSS(IntPtr handle);
    [DllImport("*", EntryPoint = "spiSSEnable")]
    private static extern void spiSSEnable(IntPtr handle);
    [DllImport("*", EntryPoint = "spiSSDisable")]
    private static extern void spiSSDisable(IntPtr handle);
    [DllImport("*", EntryPoint = "spiGetClockDiv")]
    private static extern uint spiGetClockDiv(IntPtr handle);
    [DllImport("*", EntryPoint = "spiSetClockDiv")]
    private static extern void spiSetClockDiv(IntPtr handle, uint divider);
    [DllImport("*", EntryPoint = "spiSetDataMode")]
    private static extern void spiSetDataMode(IntPtr handle, byte mode);
    [DllImport("*", EntryPoint = "spiSetBitOrder")]
    private static extern void spiSetBitOrder(IntPtr handle, byte order);
    [DllImport("*", EntryPoint = "spiTransaction")]
    private static extern void spiTransaction(IntPtr handle, uint divider, byte mode, byte order);
    [DllImport("*", EntryPoint = "spiEndTransaction")]
    private static extern void spiEndTransaction(IntPtr handle);
    [DllImport("*", EntryPoint = "spiSimpleTransaction")]
    private static extern void spiSimpleTransaction(IntPtr handle);
    [DllImport("*", EntryPoint = "spiWriteByte")]
    private static extern void spiWriteByte(IntPtr handle, byte data);
    [DllImport("*", EntryPoint = "spiWriteByteNL")]
    private static extern void spiWriteByteNL(IntPtr handle, byte data);
    [DllImport("*", EntryPoint = "spiWriteWord")]
    private static extern void spiWriteWord(IntPtr handle, ushort data);
    [DllImport("*", EntryPoint = "spiWriteShortNL")]
    private static extern void spiWriteShortNL(IntPtr handle, ushort data);
    [DllImport("*", EntryPoint = "spiWriteLong")]
    private static extern void spiWriteLong(IntPtr handle, uint data);
    [DllImport("*", EntryPoint = "spiWriteLongNL")]
    private static extern void spiWriteLongNL(IntPtr handle, uint data);
    [DllImport("*", EntryPoint = "spiTransferByte")]
    private static extern byte spiTransferByte(IntPtr handle, byte data);
    [DllImport("*", EntryPoint = "spiTransferByteNL")]
    private static extern byte spiTransferByteNL(IntPtr handle, byte data);
    [DllImport("*", EntryPoint = "spiTransferWord")]
    private static extern ushort spiTransferWord(IntPtr handle, ushort data);
    [DllImport("*", EntryPoint = "spiTransferShortNL")]
    private static extern ushort spiTransferShortNL(IntPtr handle, ushort data);
    [DllImport("*", EntryPoint = "spiTransferLong")]
    private static extern uint spiTransferLong(IntPtr handle, uint data);
    [DllImport("*", EntryPoint = "spiTransferLongNL")]
    private static extern uint spiTransferLongNL(IntPtr handle, uint data);
    [DllImport("*", EntryPoint = "spiTransferBytes")]
    private static extern void spiTransferBytes(IntPtr handle, byte* data, byte* output, uint size);
    [DllImport("*", EntryPoint = "spiTransferBytesNL")]
    private static extern void spiTransferBytesNL(IntPtr handle, void* data, byte* output, uint size);
    [DllImport("*", EntryPoint = "spiTransferBits")]
    private static extern void spiTransferBits(IntPtr handle, uint data, uint* output, byte bits);
    [DllImport("*", EntryPoint = "spiTransferBitsNL")]
    private static extern void spiTransferBitsNL(IntPtr handle, uint data, uint* output, byte bits);
    [DllImport("*", EntryPoint = "spiWriteNL")]
    private static extern void spiWriteNL(IntPtr handle, void* data, uint size);
    [DllImport("*", EntryPoint = "spiWritePixelsNL")]
    private static extern void spiWritePixelsNL(IntPtr handle, void* data, uint size);
}
