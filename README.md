
# M5Stack CoreInk デジタル時計実装プロジェクト

M5Stack CoreInk を使用し、`.NET nanoFramework` 環境下で外部依存ライブラリ（Wi-FiやRTC専用ドライバー）を一切使わずに、標準のGPIO/SPI制御だけでパキッと正確に動作する7セグメント風デジタル時計の実装ログと最終成果物です。

## 開発の背景と罠（Pitfalls）

M5Stack CoreInkに搭載されている電子ペーパー（GDEW0154M09）を `.NET nanoFramework` で制御する際、ドキュメント通りに実装を進めるといくつかの特有の罠に衝突します。本プロジェクトではそれらを力技と泥臭い検証でクリアしました。

### 1. `Math` ライブラリの不在

nanoFrameworkのコアライブラリには `System.Math` が標準含まれていません。フォントレンダリングや斜め線の計算（三角関数など）が使えないため、すべての数字を **`DrawPixel` による直線プロット（7セグメント方式）** で自前実装しました。

### 2. 解像度バッファの不整合と画面真っ暗問題

ドライバ側の初期化メソッド `screen.Clear(false)` を使用すると、内部バッファの極性（白黒フラグ）や想定解像度のズレにより、画面全体が真っ黒に染まる現象が発生します。
診断コードによる検証の結果、以下の仕様が確定しました。

* **`true`** = 黒インク（プロットON）
* **`false`** = 白インク（背景・消去）

> **解決策:** `Clear()` メソッドには頼らず、描画サイクルごとに **200×200 のループを回して手動で全面に `false`（白）を書き込んでキャンバスを構築** してから数字を置くことで、完全な同期に成功しました。

### 3. ハードウェアリセットの重要性

電子ペーパーコントローラの起床（パワーオンシーケンス）には物理的な時間がかかります。リセット（GPIO 0）を叩いた後の `Thread.Sleep(200);` は、BUSYピン（GPIO 4）の状態を安定させ、直後のSPIコマンドを確実に受容させるために削れない必須のウェイトです。

---

## 最終確定ソースコード（自己完結スタンドアロン版）

NuGetパッケージの依存関係は、標準の `System.Device.Gpio`、`System.Device.Spi`、および [Iot.Device.EPaper.Drivers.Jd796xx](https://docs.nanoframework.net/devices/Iot.Device.EPaper.Drivers.Jd796xx.Gdew0154m09.html) のみです。Wi-Fi（NTP）やRTCの参照エラーに悩まされることなく、100%ビルドが通ります。

```cs
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

            // 2. 起動時の暫定時間をセット（デプロイ時に現在時刻に合わせる）
            int currentHour = 10;
            int currentMinute = 45;

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

            // 5. ドライバーインスタンス生成
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

                // 7. 実績のある手動全面ホワイトベースクリア
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

                // 8. 安全圏内（X: 10〜120、Y: 20〜55）へのプロット
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

                // 9. 確定・一括転送
                screen.Flush();
                screen.PerformFullRefresh(); 

                // 10. パネルシャットダウン (E-Inkの表示維持特性を活かす)
                screen.PowerDown();

                // メモリリーク防止
                nanoFramework.Runtime.Native.GC.Run(true);
                
                // 1分間待機
                Thread.Sleep(60000);

                // 11. 自前時間インクリメント
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

```

---

## 今後の展望（TODO）

* 開発環境（NuGetマネージャー等）のネットワーク構成が解決し次第、`nanoFramework.Networking.Wireless80211` パッケージを導入し、NTPによる完全自動時刻同期（JSTアジャスト版）に移行する。

## 参考

- [Class Jd79653A](https://docs.nanoframework.net/devices/Iot.Device.EPaper.Drivers.Jd796xx.Jd79653A.html#Iot_Device_EPaper_Drivers_Jd796xx_Jd79653A_DrawPixel_System_Int32_System_Int32_System_Boolean)
- [Class Gdew0154m09](https://docs.nanoframework.net/devices/Iot.Device.EPaper.Drivers.Jd796xx.Gdew0154m09.html)
- [M5CoreInk](https://docs.m5stack.com/en/core/coreink)
