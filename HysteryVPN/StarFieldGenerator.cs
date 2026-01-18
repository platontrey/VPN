using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HysteryVPN
{
    public class StarFieldGenerator
    {
        public WriteableBitmap GenerateStarField(int width, int height, int starCount = 1000)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);

            // Инициализация чёрного фона
            byte[] pixels = new byte[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = 0; // Идеально черный фон

            // Генерация звёзд разной яркости и размера
            Random rand = new Random();

            for (int i = 0; i < starCount; i++)
            {
                int x = rand.Next(width);
                int y = rand.Next(height);
                byte brightness = (byte)(rand.Next(128, 256)); // Звёзды от серого до белого

                // Основная звезда
                pixels[y * width + x] = brightness;
            }

            // ЗАКОММЕНТИРУЙТЕ ЭТУ СТРОКУ:
            // AddSpaceNoise(pixels, width, height, 0.1);

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width, 0);
            bitmap.Freeze();

            return bitmap;
        }

        private void AddSpaceNoise(byte[] pixels, int width, int height, double intensity)
        {
            Random rand = new Random();
            PerlinNoise noise = new PerlinNoise();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double n = noise.Perlin(x * 0.01, y * 0.01, 0) * intensity;
                    int index = y * width + x;
                    pixels[index] = (byte)Math.Clamp(pixels[index] + n * 50, 0, 255);
                }
            }
        }
    }
}