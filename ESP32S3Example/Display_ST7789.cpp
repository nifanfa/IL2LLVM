#include "Display_ST7789.h"
   
SPIClass LCDspi(FSPI);
#define SPI_WRITE(_dat)         LCDspi.transfer(_dat)
#define SPI_WRITE_Word(_dat)    LCDspi.transfer16(_dat)
void SPI_Init()
{
  LCDspi.begin(EXAMPLE_PIN_NUM_LCD_SCLK, EXAMPLE_PIN_NUM_LCD_MISO,
               EXAMPLE_PIN_NUM_LCD_MOSI, -1);
}

void LCD_WriteCommand(uint8_t Cmd)  
{ 
  LCDspi.beginTransaction(SPISettings(SPIFreq, MSBFIRST, SPI_MODE0));
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, LOW);  
  digitalWrite(EXAMPLE_PIN_NUM_LCD_DC, LOW); 
  SPI_WRITE(Cmd);
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, HIGH);  
  LCDspi.endTransaction();
}
void LCD_WriteData(uint8_t Data) 
{ 
  LCDspi.beginTransaction(SPISettings(SPIFreq, MSBFIRST, SPI_MODE0));
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, LOW);  
  digitalWrite(EXAMPLE_PIN_NUM_LCD_DC, HIGH);  
  SPI_WRITE(Data);  
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, HIGH);  
  LCDspi.endTransaction();
}    
void LCD_WriteData_Word(uint16_t Data)
{
  LCDspi.beginTransaction(SPISettings(SPIFreq, MSBFIRST, SPI_MODE0));
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, LOW);  
  digitalWrite(EXAMPLE_PIN_NUM_LCD_DC, HIGH); 
  SPI_WRITE_Word(Data);
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, HIGH);  
  LCDspi.endTransaction();
}   
void LCD_WriteData_nbyte(uint8_t* SetData,uint8_t* ReadData,uint32_t Size) 
{ 
  LCDspi.beginTransaction(SPISettings(SPIFreq, MSBFIRST, SPI_MODE0));
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, LOW);  
  digitalWrite(EXAMPLE_PIN_NUM_LCD_DC, HIGH);  
  LCDspi.transferBytes(SetData, ReadData, Size);
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, HIGH);  
  LCDspi.endTransaction();
} 

void LCD_Reset(void)
{
  if (EXAMPLE_PIN_NUM_LCD_RST >= 0) {
    digitalWrite(EXAMPLE_PIN_NUM_LCD_RST, LOW);
    delay(50);
    digitalWrite(EXAMPLE_PIN_NUM_LCD_RST, HIGH);
    delay(120);
  } else {
    LCD_WriteCommand(0x01); // SWRESET: RST is not wired
    delay(150);
  }
}
void LCD_Init(void)
{
  pinMode(EXAMPLE_PIN_NUM_LCD_CS, OUTPUT);
  digitalWrite(EXAMPLE_PIN_NUM_LCD_CS, HIGH);
  pinMode(EXAMPLE_PIN_NUM_LCD_DC, OUTPUT);
  digitalWrite(EXAMPLE_PIN_NUM_LCD_DC, HIGH);
  if (EXAMPLE_PIN_NUM_LCD_RST >= 0) {
    pinMode(EXAMPLE_PIN_NUM_LCD_RST, OUTPUT);
    digitalWrite(EXAMPLE_PIN_NUM_LCD_RST, HIGH);
  }
  Backlight_Init();
  SPI_Init();

  LCD_Reset();
  //************* Start Initial Sequence **********// 
  LCD_WriteCommand(0x11);
  delay(120);
  LCD_WriteCommand(0x36);
  LCD_WriteData(LCD_MADCTL);

  LCD_WriteCommand(0x3A);
  LCD_WriteData(0x05);

  LCD_WriteCommand(0xB0);
  LCD_WriteData(0x00);
  LCD_WriteData(0xE8);
  
  LCD_WriteCommand(0xB2);
  LCD_WriteData(0x0C);
  LCD_WriteData(0x0C);
  LCD_WriteData(0x00);
  LCD_WriteData(0x33);
  LCD_WriteData(0x33);

  LCD_WriteCommand(0xB7);
  LCD_WriteData(0x35);

  LCD_WriteCommand(0xBB);
  LCD_WriteData(0x35);

  LCD_WriteCommand(0xC0);
  LCD_WriteData(0x2C);

  LCD_WriteCommand(0xC2);
  LCD_WriteData(0x01);

  LCD_WriteCommand(0xC3);
  LCD_WriteData(0x13);

  LCD_WriteCommand(0xC4);
  LCD_WriteData(0x20);

  LCD_WriteCommand(0xC6);
  LCD_WriteData(0x0F);

  LCD_WriteCommand(0xD0);
  LCD_WriteData(0xA4);
  LCD_WriteData(0xA1);

  LCD_WriteCommand(0xD6);
  LCD_WriteData(0xA1);

  LCD_WriteCommand(0xE0);
  LCD_WriteData(0xF0);
  LCD_WriteData(0x00);
  LCD_WriteData(0x04);
  LCD_WriteData(0x04);
  LCD_WriteData(0x04);
  LCD_WriteData(0x05);
  LCD_WriteData(0x29);
  LCD_WriteData(0x33);
  LCD_WriteData(0x3E);
  LCD_WriteData(0x38);
  LCD_WriteData(0x12);
  LCD_WriteData(0x12);
  LCD_WriteData(0x28);
  LCD_WriteData(0x30);

  LCD_WriteCommand(0xE1);
  LCD_WriteData(0xF0);
  LCD_WriteData(0x07);
  LCD_WriteData(0x0A);
  LCD_WriteData(0x0D);
  LCD_WriteData(0x0B);
  LCD_WriteData(0x07);
  LCD_WriteData(0x28);
  LCD_WriteData(0x33);
  LCD_WriteData(0x3E);
  LCD_WriteData(0x36);
  LCD_WriteData(0x14);
  LCD_WriteData(0x14);
  LCD_WriteData(0x29);
  LCD_WriteData(0x32);

  LCD_WriteCommand(0x21);

  LCD_WriteCommand(0x11);
  delay(120);
  LCD_WriteCommand(0x29); 
}
/******************************************************************************
function: Set the cursor position
parameter :
    Xstart:   Start uint16_t x coordinate
    Ystart:   Start uint16_t y coordinate
    Xend  :   End uint16_t coordinates
    Yend  :   End uint16_t coordinatesen
******************************************************************************/
void LCD_SetCursor(uint16_t Xstart, uint16_t Ystart, uint16_t Xend, uint16_t  Yend)
{ 
  uint16_t colStart = Xstart + Offset_X;
  uint16_t colEnd = Xend + Offset_X;

  LCD_WriteCommand(0x2A);
  LCD_WriteData(colStart >> 8);
  LCD_WriteData(colStart);
  LCD_WriteData(colEnd >> 8);
  LCD_WriteData(colEnd);

  LCD_WriteCommand(0x2B);
  uint16_t rowStart = Ystart + Offset_Y;
  uint16_t rowEnd = Yend + Offset_Y;
  LCD_WriteData(rowStart >> 8);
  LCD_WriteData(rowStart);
  LCD_WriteData(rowEnd >> 8);
  LCD_WriteData(rowEnd);

  LCD_WriteCommand(0x2C);
}
/******************************************************************************
function: Refresh the image in an area
parameter :
    Xstart:   Start uint16_t x coordinate
    Ystart:   Start uint16_t y coordinate
    Xend  :   End uint16_t coordinates
    Yend  :   End uint16_t coordinates
    color :   Set the color
******************************************************************************/
void LCD_addWindow(uint16_t Xstart, uint16_t Ystart, uint16_t Xend, uint16_t Yend,uint16_t* color)
{             
  uint16_t Show_Width = Xend - Xstart + 1;
  uint16_t Show_Height = Yend - Ystart + 1;
  uint32_t numBytes = Show_Width * Show_Height * sizeof(uint16_t);
  uint8_t Read_D[numBytes];
  LCD_SetCursor(Xstart, Ystart, Xend, Yend);
  LCD_WriteData_nbyte((uint8_t*)color, Read_D, numBytes);        
}
// backlight
uint8_t LCD_Backlight = 90;
void Backlight_Init(void)
{
  ledcAttach(EXAMPLE_PIN_NUM_LCD_BL, Frequency, Resolution);
  Set_Backlight(LCD_Backlight);      //0~100    
}

void Set_Backlight(uint8_t Light)                        //
{

  if(Light > Backlight_MAX)
    printf("Set Backlight parameters in the range of 0 to 100 \r\n");
  else{
    uint32_t Backlight = Light*10;
    if(Backlight == 1000)
      Backlight = 1024;
    ledcWrite(EXAMPLE_PIN_NUM_LCD_BL, Backlight);
  }
}





