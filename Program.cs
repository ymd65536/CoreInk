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

            // 1. CoreInk 電源保持ピン (GPIO 12) を最優先で High 固定
            GpioPin powerHoldPin = gpio.OpenPin(12, PinMode.Output);
            powerHoldPin.Write(PinValue.High);
            Thread.Sleep(100);

            // 2. 起動時の暫定時間をセット（ここを現在の時刻に書き換えてビルドすれば即座に合います）
            int currentHour = 10;
            int currentMinute = 43;

            // 3. SPIピンマッピング (MOSI=23, CLK=18)
            Configuration.SetPinFunction(23, DeviceFunction.SPI1_MOSI);
            Configuration.SetPinFunction(18, DeviceFunction.SPI1_CLOCK);

            // 4. SPI設定
            var connectionSettings = new SpiConnectionSettings(1, -1)
            {
                ClockFrequency = 4000000,
                Mode = Gdew0154m09.SpiMode
            };
            using SpiDevice spiDevice = SpiDevice.Create(connectionSettings);

            // CSピン (GPIO 9) を Low 固定
            GpioPin csPin = gpio.OpenPin(9, PinMode.Output);
            csPin.Write(PinValue.Low);

            // 5. ドライバーインスタンス生成 (一括バッファモード)
            using Gdew0154m09 screen = new Gdew0154m09(
                spiDevice,
                resetPin: -1,
                busyPin: 4,
                dataCommandPin: 15,
                gpioController: gpio,
                enableFramePaging: false
            );

            // 7セグメント配列テーブル (0〜9)
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

            Console.WriteLine("Pure Isolated Clock System Started.");

            while (true)
            {
                // 6. ハードウェアリセット
                using (GpioPin rstPin = gpio.OpenPin(0, PinMode.Output))
                {
                    rstPin.Write(PinValue.Low); Thread.Sleep(20);
                    rstPin.Write(PinValue.High); Thread.Sleep(200);
                }

                screen.Initialize();
                screen.PowerOn();

                // 実績のある手動全面ホワイトベースクリア
                for (int x = 0; x < 200; x++)
                {
                    for (int y = 0; y < 200; y++)
                    {
                        screen.DrawPixel(x, y, false);
                    }
                }

                // 桁データの展開
                int[] digits = new int[4] {
                    currentHour / 10,
                    currentHour % 10,
                    currentMinute / 10,
                    currentMinute % 10
                };
                Console.WriteLine($"Render Static Time: {digits[0]}{digits[1]}:{digits[2]}{digits[3]}");

                // 7. 安全圏内（X: 10〜120、Y: 20〜55）へのプロット
                int w = 16; int h = 30; int h2 = h / 2;
                for (int k = 0; k < 4; k++)
                {
                    int startX = 10 + (k * 24);
                    if (k >= 2) startX += 12;

                    int num = digits[k];
                    bool[] activeSeg = segments[num];

                    if (activeSeg[0]) { for (int i = 0; i < w; i++) { screen.DrawPixel(startX + i, 20, true); screen.DrawPixel(startX + i, 21, true); } }
                    if (activeSeg[1]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX + w, 20 + i, true); screen.DrawPixel(startX + w + 1, 20 + i, true); } }
                    if (activeSeg[2]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX + w, 20 + h2 + i, true); screen.DrawPixel(startX + w + 1, 20 + h2 + i, true); } }
                    if (activeSeg[3]) { for (int i = 0; i < w; i++) { screen.DrawPixel(startX + i, 20 + h, true); screen.DrawPixel(startX + i, 20 + h + 1, true); } }
                    if (activeSeg[4]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX, 20 + h2 + i, true); screen.DrawPixel(startX + 1, 20 + h2 + i, true); } }
                    if (activeSeg[5]) { for (int i = 0; i < h2; i++) { screen.DrawPixel(startX, 20 + i, true); screen.DrawPixel(startX + 1, 20 + i, true); } }
                    if (activeSeg[6]) { for (int i = 0; i < w; i++) { screen.DrawPixel(startX + i, 20 + h2, true); screen.DrawPixel(startX + i, 20 + h2 + 1, true); } }
                }

                // コロン「:」の描画
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

                // 9. パネルシャットダウン
                screen.PowerDown();

                // ガベージコレクションを明示的に走らせる
                nanoFramework.Runtime.Native.GC.Run(true);

                // 1分間待機
                Thread.Sleep(60000);

                // 10. 自前で時間をインクリメント
                currentMinute++;
                if (currentMinute >= 60)
                {
                    currentMinute = 0;
                    currentHour++;
                    if (currentHour >= 24)
                    {
                        currentHour = 0;
                    }
                }
            }
        }
    }
}
