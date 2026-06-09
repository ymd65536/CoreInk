using System;
using System.Threading;
using nanoFramework.M5Stack;

namespace M5CoreInkApp
{
    public class Program
    {
        public static void Main()
        {
            // 1. 画面の初期化
            M5CoreInk.Screen.Clear();
            Console.WriteLine("M5CoreInk App Started!");
            // アプリケーションが終了しないようにスレッドを維持
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
