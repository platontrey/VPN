using System;

namespace HysteryVPN
{
    public class PerlinNoise
    {
        private int[] permutation = new int[512];

        public PerlinNoise(int seed = 0)
        {
            Random rand = new Random(seed);
            int[] p = new int[256];

            for (int i = 0; i < 256; i++) p[i] = i;

            // Перемешиваем массив
            for (int i = 0; i < 256; i++)
            {
                int j = rand.Next(i, 256);
                int temp = p[i];
                p[i] = p[j];
                p[j] = temp;
            }

            // Дублируем для быстрого доступа
            for (int i = 0; i < 512; i++)
            {
                permutation[i] = p[i & 255];
            }
        }

        public double Perlin(double x, double y, double z)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            int zi = (int)Math.Floor(z) & 255;

            double xf = x - Math.Floor(x);
            double yf = y - Math.Floor(y);
            double zf = z - Math.Floor(z);

            double u = Fade(xf);
            double v = Fade(yf);
            double w = Fade(zf);

            int aaa = permutation[permutation[permutation[xi] + yi] + zi];
            int aba = permutation[permutation[permutation[xi] + yi + 1] + zi];
            int aab = permutation[permutation[permutation[xi] + yi] + zi + 1];
            int abb = permutation[permutation[permutation[xi] + yi + 1] + zi + 1];
            int baa = permutation[permutation[permutation[xi + 1] + yi] + zi];
            int bba = permutation[permutation[permutation[xi + 1] + yi + 1] + zi];
            int bab = permutation[permutation[permutation[xi + 1] + yi] + zi + 1];
            int bbb = permutation[permutation[permutation[xi + 1] + yi + 1] + zi + 1];

            double x1 = Lerp(u, Grad(aaa, xf, yf, zf), Grad(baa, xf - 1, yf, zf));
            double x2 = Lerp(u, Grad(aba, xf, yf - 1, zf), Grad(bba, xf - 1, yf - 1, zf));
            double y1 = Lerp(v, x1, x2);

            x1 = Lerp(u, Grad(aab, xf, yf, zf - 1), Grad(bab, xf - 1, yf, zf - 1));
            x2 = Lerp(u, Grad(abb, xf, yf - 1, zf - 1), Grad(bbb, xf - 1, yf - 1, zf - 1));
            double y2 = Lerp(v, x1, x2);

            return (Lerp(w, y1, y2) + 1) / 2;
        }

        private double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private double Lerp(double t, double a, double b) => a + t * (b - a);
        private double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}