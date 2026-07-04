using System;
using System.Threading;
using System.Device.Gpio;
using System.Device.Spi;
using nanoFramework.Hardware.Esp32;
using Iot.Device.EPaper.Drivers.Jd796xx;

namespace M5CoreInkApp
{
    public class Program
    {
        public static void Main()
        {
            using GpioController gpio = new GpioController();

            // 1. CoreInk 電源保持ピン (GPIO 12) を High 固定
            GpioPin powerHoldPin = gpio.OpenPin(12, PinMode.Output);
            powerHoldPin.Write(PinValue.High);
            Thread.Sleep(100);

            // 2. SPIピンマッピング (MOSI=23, CLK=18)
            Configuration.SetPinFunction(23, DeviceFunction.SPI1_MOSI);
            Configuration.SetPinFunction(18, DeviceFunction.SPI1_CLOCK);

            // 3. SPI設定
            var connectionSettings = new SpiConnectionSettings(1, -1)
            {
                ClockFrequency = 4000000,
                Mode = Gdew0154m09.SpiMode
            };
            using SpiDevice spiDevice = SpiDevice.Create(connectionSettings);

            // 4. CSピン (GPIO 9) を Low 固定
            GpioPin csPin = gpio.OpenPin(9, PinMode.Output);
            csPin.Write(PinValue.Low);

            // 5. ドライバーインスタンスの固定
            using Gdew0154m09 screen = new Gdew0154m09(
                spiDevice,
                resetPin: -1,
                busyPin: 4,
                dataCommandPin: 15,
                gpioController: gpio,
                enableFramePaging: false
            );

            // 7セグメントの点灯パターンテーブル (0〜9)
            bool[][] segments = new bool[][]
            {
                new bool[] { true,  true,  true,  true,  true,  true,  false }, // 0
                new bool[] { false, true,  true,  false, false, false, false }, // 1
                new bool[] { true,  true,  false, true,  true,  false, true  }, // 2
                new bool[] { true,  true,  true,  true,  false, false, true  }, // 3
                new bool[] { false, true,  true,  false, false, true,  true  }, // 4
                new bool[] { true,  false, true,  true,  false, true,  true  }, // 5
                new bool[] { true,  false, true,  true,  true,  true,  true  }, // 6
                new bool[] { true,  true,  true,  false, false, false, false }, // 7
                new bool[] { true,  true,  true,  true,  true,  true,  true  }, // 8
                new bool[] { true,  true,  true,  true,  false, true,  true  }  // 9
            };

            Console.WriteLine("Clock Application Started.");

            while (true)
            {
                // 6. ハードウェアリセット (安定起床ウェイト 200ms)
                using (GpioPin rstPin = gpio.OpenPin(0, PinMode.Output))
                {
                    rstPin.Write(PinValue.Low);
                    Thread.Sleep(20);
                    rstPin.Write(PinValue.High);
                    Thread.Sleep(200);
                }

                screen.Initialize();
                screen.PowerOn();

                // 診断コードで実績の出た「手動全面ホワイトベース構築」を展開
                for (int x = 0; x < 200; x++)
                {
                    for (int y = 0; y < 200; y++)
                    {
                        screen.DrawPixel(x, y, false);
                    }
                }

                // 現在時刻を取得して桁分解
                DateTime now = DateTime.UtcNow.AddHours(9);
                int[] digits = new int[4] {
                    now.Hour / 10,
                    now.Hour % 10,
                    now.Minute / 10,
                    now.Minute % 10
                };
                Console.WriteLine($"Render Time: {digits[0]}{digits[1]}:{digits[2]}{digits[3]}");

                // 7. 安全圏内（X: 10〜120, Y: 20〜55）へのプロット
                int w = 16;
                int h = 30;
                int h2 = h / 2;

                for (int k = 0; k < 4; k++)
                {
                    int startX = 10 + (k * 24);
                    if (k >= 2) startX += 12; // コロン用のオフセット

                    int num = digits[k];
                    bool[] activeSeg = segments[num];

                    // 各セグメントを true (黒) で上書き描画します
                    // A (上辺)
                    if (activeSeg[0]) { for (int i = 0; i < w; i++) { screen.DrawPixel(startX + i, 20, true); screen.DrawPixel(startX + i, 21, true); } }
                    // B (右上)
                    if (activeSeg[1]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX + w, 20 + i, true); screen.DrawPixel(startX + w + 1, 20 + i, true); } }
                    // C (右下)
                    if (activeSeg[2]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX + w, 20 + h2 + i, true); screen.DrawPixel(startX + w + 1, 20 + h2 + i, true); } }
                    // D (底辺)
                    if (activeSeg[3]) { for (int i = 0; i < w; i++) { screen.DrawPixel(startX + i, 20 + h, true); screen.DrawPixel(startX + i, 20 + h + 1, true); } }
                    // E (左下)
                    if (activeSeg[4]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX, 20 + h2 + i, true); screen.DrawPixel(startX + 1, 20 + h2 + i, true); } }
                    // F (左上)
                    if (activeSeg[5]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX, 20 + i, true); screen.DrawPixel(startX + 1, 20 + i, true); } }
                    // G (中央)
                    if (activeSeg[6]) { for (int i = 0; i < w; i++) { screen.DrawPixel(startX + i, 20 + h2, true); screen.DrawPixel(startX + i, 20 + h2 + 1, true); } }
                }

                // コロン「:」の描画 (true = 黒)
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        screen.DrawPixel(58 + i, 28 + j, true);
                        screen.DrawPixel(58 + i, 40 + j, true);
                    }
                }

                // 8. 確定・一括転送
                screen.Flush();
                screen.PerformFullRefresh();

                // 9. パネルシャットダウン（物理的な画面描画を保持）
                screen.PowerDown();

                nanoFramework.Runtime.Native.GC.Run(true);
                Thread.Sleep(60000);
            }
        }
    }
}