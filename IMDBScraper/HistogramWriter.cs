using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace IMDBScraper
{
    public static class HistogramWriter
    {
        static readonly char[] bar = new char[] { ' ', '\u2584', '\u2588' };
        static readonly char yAxis = '\u2502';
        static readonly char xAxis = '\u2500';
        static readonly char origin = '\u2514';

        private static int[] bucketize(float[] data, float min, float max, int buckets)
        {
            var output = new int[buckets];

            for (int i = 0; i < data.Length; i++)
            {
                float x = data[i];
                float xNorm = (x - min) / (max - min);
                var bucket = (int)(xNorm * (buckets - 1));

                if(bucket >= 0 && bucket < buckets)
                    output[bucket]++;
            }

            return output;
        }

        private static void normalize(int[] buckets, int range)
        {
            var yMax = buckets.Max();
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = (buckets[i] * range) / yMax;
        }

        public static void WriteHistogram<T>(this TextWriter writer, string title, ICollection<T> values, int outputWidth, int outputHeight) where T : struct, IConvertible
        {
            var pts = values.Select(x => x.ToSingle(CultureInfo.InvariantCulture)).ToArray();
            var bucketCount = outputWidth - 2;

            int lowerBound, upperBound;

            if (pts.Length == 0) return;

            var min = pts.Min();
            var max = pts.Max();
            var barMax = (outputHeight - 3) * (bar.Length - 1);
            int[] buckets;

            int bucketLoop = 0;
            do
            {
                buckets = bucketize(pts, min, max, bucketCount);
                normalize(buckets, barMax);

                // Trim useless area
                for (lowerBound = 0; buckets[lowerBound] == 0 && lowerBound < buckets.Length - 1; lowerBound++) ;
                for (upperBound = buckets.Length - 1; buckets[upperBound] == 0 && upperBound >= 0; upperBound--) ;

                var step = (max - min) / buckets.Length;

                // Correct min and max to better values, then regenerate the histogram
                max = min + step * (upperBound + 1);
                min = min + step * lowerBound;
                bucketLoop++;
            } while (bucketLoop < 2);

            var yMax = buckets.Max();
            var yMaxText = yMax.ToString();
            var spaces = Math.Max(0, (outputWidth - title.Length) / 2 - 3);

            writer.WriteLine();
            writer.Write(yMax);
            writer.Write(new string(' ', spaces));
            writer.WriteLine($"-- {title} --");

            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = (buckets[i] * barMax) / yMax;

            for (int y = barMax; y > 0; y -= 8)
            {
                writer.Write(yAxis);
                for(int i = 0;i < buckets.Length;i++)
                {
                    writer.Write(bar[Math.Max(0, Math.Min(bar.Length - 1, buckets[i] - y + bar.Length - 1))]);
                }
                writer.WriteLine();
            }

            writer.Write(origin);
            for (int i = 0; i < buckets.Length; i++)
                writer.Write(xAxis);
            writer.WriteLine();

            var minString = min.ToString("0.000");
            var maxString = max.ToString("0.000");

            writer.WriteLine($"{minString}{new string(' ', outputWidth - minString.Length - maxString.Length - 1)}{maxString}");
        }
    }
}
