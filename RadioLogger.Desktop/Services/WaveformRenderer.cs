using ManagedBass;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadioLogger.Services
{
    /// <summary>
    /// Mipmap level: pre-computed min/max/rms at a specific reduction factor.
    /// </summary>
    public class WaveformMipLevel
    {
        public float[] MaxValues { get; }
        public float[] MinValues { get; }
        public float[] RmsValues { get; }
        public int PointCount { get; }
        public int SamplesPerPoint { get; }

        public WaveformMipLevel(float[] max, float[] min, float[] rms, int count, int samplesPerPoint)
        {
            MaxValues = max;
            MinValues = min;
            RmsValues = rms;
            PointCount = count;
            SamplesPerPoint = samplesPerPoint;
        }
    }

    /// <summary>
    /// Multi-resolution waveform cache. Stores multiple detail levels
    /// plus a reference to the audio file for raw sample access at extreme zoom.
    /// </summary>
    public class WaveformData
    {
        public List<WaveformMipLevel> Levels { get; } = new();
        public long TotalMonoSamples { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public string FilePath { get; set; } = "";
    }

    public static class WaveformRenderer
    {
        // Mipmap reduction factors: each level has N samples per data point
        // Level 0 = finest pre-computed (64 samples/point), up to coarsest
        private static readonly int[] MipFactors = { 64, 256, 1024, 4096, 16384, 65536 };

        /// <summary>
        /// Builds multi-resolution waveform cache from audio file.
        /// </summary>
        public static Task<WaveformData?> ExtractDataAsync(string filePath)
        {
            return Task.Run(() => ExtractData(filePath));
        }

        /// <summary>
        /// Builds multi-resolution waveform cache from raw PCM float samples already in memory.
        /// Used for concatenated audio preview.
        /// </summary>
        public static Task<WaveformData?> ExtractDataFromPcmAsync(float[] pcm, int sampleRate, int channels)
        {
            return Task.Run(() => ExtractDataFromPcm(pcm, sampleRate, channels));
        }

        private static WaveformData? ExtractDataFromPcm(float[] pcm, int sampleRate, int channels)
        {
            if (pcm == null || pcm.Length == 0) return null;

            // Downmix to mono if stereo
            float[] mono;
            if (channels == 1)
            {
                mono = pcm;
            }
            else
            {
                int monoLen = pcm.Length / channels;
                mono = new float[monoLen];
                for (int i = 0; i < monoLen; i++)
                {
                    float val = pcm[i * channels];
                    if (channels > 1 && i * channels + 1 < pcm.Length)
                        val = (val + pcm[i * channels + 1]) / 2f;
                    mono[i] = val;
                }
            }

            return BuildMipmaps(mono, sampleRate, channels, "");
        }

        private static WaveformData? ExtractData(string filePath)
        {
            int stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (stream == 0) return null;

            try
            {
                long totalBytes = Bass.ChannelGetLength(stream);
                var info = Bass.ChannelGetInfo(stream);
                int channels = info.Channels;
                long totalMonoSamples = totalBytes / sizeof(float) / channels;

                int readChunkSize = 65536 * channels;
                float[] readBuffer = new float[readChunkSize];
                var allSamples = new List<float>((int)Math.Min(totalMonoSamples, int.MaxValue));

                while (true)
                {
                    int got = Bass.ChannelGetData(stream, readBuffer, readChunkSize * sizeof(float));
                    if (got <= 0) break;
                    int samplesRead = got / sizeof(float);

                    for (int i = 0; i < samplesRead; i += channels)
                    {
                        float val = readBuffer[i];
                        if (channels > 1 && i + 1 < samplesRead)
                            val = (val + readBuffer[i + 1]) / 2f;
                        allSamples.Add(val);
                    }
                }

                float[] mono = allSamples.ToArray();
                return BuildMipmaps(mono, info.Frequency, channels, filePath);
            }
            finally
            {
                Bass.StreamFree(stream);
            }
        }

        private static WaveformData BuildMipmaps(float[] mono, int sampleRate, int channels, string filePath)
        {
            int monoCount = mono.Length;

            var data = new WaveformData
            {
                TotalMonoSamples = monoCount,
                SampleRate = sampleRate,
                Channels = channels,
                FilePath = filePath
            };

            foreach (int factor in MipFactors)
            {
                if (monoCount / factor < 10) break;

                int points = monoCount / factor;
                float[] maxArr = new float[points];
                float[] minArr = new float[points];
                float[] rmsArr = new float[points];

                for (int p = 0; p < points; p++)
                {
                    int start = p * factor;
                    int end = Math.Min(start + factor, monoCount);
                    float max = float.MinValue, min = float.MaxValue, sum = 0;
                    int count = 0;

                    for (int s = start; s < end; s++)
                    {
                        float v = mono[s];
                        if (v > max) max = v;
                        if (v < min) min = v;
                        sum += v * v;
                        count++;
                    }

                    maxArr[p] = max;
                    minArr[p] = min;
                    rmsArr[p] = count > 0 ? MathF.Sqrt(sum / count) : 0;
                }

                data.Levels.Add(new WaveformMipLevel(maxArr, minArr, rmsArr, points, factor));
            }

            data.Levels.Insert(0, new WaveformMipLevel(mono, mono, mono, monoCount, 1));
            return data;
        }

        /// <summary>
        /// Selects the best mipmap level for the visible range and pixel width.
        /// Returns the level where each pixel maps to roughly 1-4 data points (optimal detail).
        /// </summary>
        private static WaveformMipLevel SelectLevel(WaveformData data, double startRatio, double endRatio, int pixelWidth)
        {
            double visibleSamples = (endRatio - startRatio) * data.TotalMonoSamples;
            double idealSamplesPerPixel = visibleSamples / pixelWidth;

            // We want a level where samplesPerPoint <= idealSamplesPerPixel
            // so each pixel has at least 1 data point (no gaps).
            // Walk from coarsest to finest and pick the first that's detailed enough.
            WaveformMipLevel best = data.Levels[data.Levels.Count - 1]; // coarsest fallback
            for (int i = data.Levels.Count - 1; i >= 0; i--)
            {
                var level = data.Levels[i];
                if (level.SamplesPerPoint <= idealSamplesPerPixel * 2)
                {
                    best = level;
                    break;
                }
            }

            return best;
        }

        /// <summary>
        /// Renders a region of the waveform using SkiaSharp with automatic LOD selection.
        /// </summary>
        public static WriteableBitmap? RenderRegion(WaveformData data, double startRatio, double endRatio, int width, int height)
        {
            if (data == null || data.Levels.Count == 0 || width <= 0 || height <= 0) return null;

            startRatio = Math.Clamp(startRatio, 0, 1);
            endRatio = Math.Clamp(endRatio, startRatio + 0.00001, 1);

            var level = SelectLevel(data, startRatio, endRatio, width);

            // Map ratio range to level indices
            int startIdx = (int)(startRatio * level.PointCount);
            int endIdx = (int)(endRatio * level.PointCount);
            startIdx = Math.Clamp(startIdx, 0, level.PointCount - 1);
            endIdx = Math.Clamp(endIdx, startIdx + 1, level.PointCount);
            int dataRange = endIdx - startIdx;

            bool isRawLevel = level.SamplesPerPoint == 1;

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            float midY = height / 2f;

            // Center line
            using var centerPaint = new SKPaint
            {
                Color = new SKColor(0x22, 0x22, 0x22),
                StrokeWidth = 1,
                IsAntialias = false
            };
            canvas.DrawLine(0, midY, width, midY, centerPaint);

            if (isRawLevel && dataRange <= width * 4)
            {
                // RAW SAMPLE MODE: draw the actual waveform as a connected line
                DrawRawWaveform(canvas, level, startIdx, endIdx, width, height, midY);
            }
            else
            {
                // OVERVIEW MODE: draw peak + RMS envelopes
                DrawEnvelopeWaveform(canvas, level, startIdx, endIdx, dataRange, width, height, midY);
            }

            // Convert SKSurface → WriteableBitmap
            using var image = surface.Snapshot();
            using var pixelData = image.PeekPixels();

            WriteableBitmap? bmp = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                bmp.Lock();
                var srcPtr = pixelData.GetPixels();
                int stride = width * 4;
                unsafe
                {
                    Buffer.MemoryCopy(srcPtr.ToPointer(), bmp.BackBuffer.ToPointer(), stride * height, stride * height);
                }
                bmp.AddDirtyRect(new Int32Rect(0, 0, width, height));
                bmp.Unlock();
            });

            return bmp;
        }

        /// <summary>
        /// Draws individual samples as a connected line — used at extreme zoom.
        /// Like Adobe Audition at max zoom: you see the actual sine wave.
        /// </summary>
        private static void DrawRawWaveform(SKCanvas canvas, WaveformMipLevel level,
            int startIdx, int endIdx, int width, int height, float midY)
        {
            int count = endIdx - startIdx;
            if (count < 2) return;

            using var path = new SKPath();

            for (int i = 0; i < count; i++)
            {
                int idx = startIdx + i;
                if (idx >= level.PointCount) break;

                float x = (i / (float)(count - 1)) * (width - 1);
                float y = midY - level.MaxValues[idx] * midY;

                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }

            // Fill area under curve
            using var fillPath = new SKPath(path);
            float lastX = ((count - 1) / (float)(count - 1)) * (width - 1);
            fillPath.LineTo(lastX, midY);
            fillPath.LineTo(0, midY);
            fillPath.Close();

            using var fillPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0x55, 0x66, 0x50),
                Style = SKPaintStyle.Fill,
                IsAntialias = false
            };
            canvas.DrawPath(fillPath, fillPaint);

            // Draw the line itself
            using var linePaint = new SKPaint
            {
                Color = new SKColor(0x00, 0xCC, 0xEE, 0xFF),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                IsAntialias = false
            };
            canvas.DrawPath(path, linePaint);

            // Draw sample dots if very few on screen
            if (count <= width / 4)
            {
                using var dotPaint = new SKPaint
                {
                    Color = new SKColor(0x55, 0xDD, 0xFF, 0xFF),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = false
                };
                for (int i = 0; i < count; i++)
                {
                    int idx = startIdx + i;
                    if (idx >= level.PointCount) break;
                    float x = (i / (float)(count - 1)) * (width - 1);
                    float y = midY - level.MaxValues[idx] * midY;
                    canvas.DrawCircle(x, y, 2f, dotPaint);
                }
            }
        }

        /// <summary>
        /// Draws peak + RMS envelopes as vertical bars per pixel — no interpolation.
        /// Each pixel column shows the true min/max of all samples in that range.
        /// </summary>
        private static void DrawEnvelopeWaveform(SKCanvas canvas, WaveformMipLevel level,
            int startIdx, int endIdx, int dataRange, int width, int height, float midY)
        {
            using var peakPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0x55, 0x66, 0x99),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = false
            };

            using var rmsPaint = new SKPaint
            {
                Color = new SKColor(0x00, 0xAA, 0xBB, 0xDD),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = false
            };

            for (int px = 0; px < width; px++)
            {
                // Calculate the range of data points that map to this pixel
                int rangeStart = startIdx + (int)((long)px * dataRange / width);
                int rangeEnd = startIdx + (int)((long)(px + 1) * dataRange / width);
                rangeStart = Math.Clamp(rangeStart, startIdx, endIdx - 1);
                rangeEnd = Math.Clamp(rangeEnd, rangeStart + 1, endIdx);

                // Find true min/max/rms across all points in this pixel's range
                float peakMax = float.MinValue;
                float peakMin = float.MaxValue;
                float rmsMax = 0;

                for (int i = rangeStart; i < rangeEnd; i++)
                {
                    if (level.MaxValues[i] > peakMax) peakMax = level.MaxValues[i];
                    if (level.MinValues[i] < peakMin) peakMin = level.MinValues[i];
                    if (level.RmsValues[i] > rmsMax) rmsMax = level.RmsValues[i];
                }

                if (peakMax == float.MinValue) continue;

                // Draw peak bar (full extent)
                float yTop = midY - peakMax * midY;
                float yBot = midY - peakMin * midY;
                canvas.DrawLine(px, yTop, px, yBot, peakPaint);

                // Draw RMS bar (centered, brighter)
                float yRmsTop = midY - rmsMax * midY;
                float yRmsBot = midY + rmsMax * midY;
                canvas.DrawLine(px, yRmsTop, px, yRmsBot, rmsPaint);
            }
        }
    }
}
