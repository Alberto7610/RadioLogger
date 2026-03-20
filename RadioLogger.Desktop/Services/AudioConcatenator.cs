using ManagedBass;
using ManagedBass.Enc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RadioLogger.Services
{
    public class ConcatenatedAudio : IDisposable
    {
        public float[] PcmData { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public double DurationSeconds { get; }
        public List<string> SourceFiles { get; }

        private int _previewStream;

        public ConcatenatedAudio(float[] pcm, int sampleRate, int channels, List<string> sourceFiles)
        {
            PcmData = pcm;
            SampleRate = sampleRate;
            Channels = channels;
            DurationSeconds = pcm.Length / (double)(sampleRate * channels);
            SourceFiles = sourceFiles;
        }

        public int CreatePreviewStream()
        {
            StopPreview();
            _previewStream = Bass.CreateStream(SampleRate, Channels, BassFlags.Float, StreamProcedureType.Push);
            if (_previewStream == 0) return 0;

            // Push all PCM data
            var bytes = new byte[PcmData.Length * sizeof(float)];
            Buffer.BlockCopy(PcmData, 0, bytes, 0, bytes.Length);
            Bass.StreamPutData(_previewStream, bytes, bytes.Length);
            Bass.StreamPutData(_previewStream, IntPtr.Zero, (int)StreamProcedureType.End);

            return _previewStream;
        }

        public void StopPreview()
        {
            if (_previewStream != 0)
            {
                Bass.ChannelStop(_previewStream);
                Bass.StreamFree(_previewStream);
                _previewStream = 0;
            }
        }

        public void Dispose()
        {
            StopPreview();
        }
    }

    public static class AudioConcatenator
    {
        /// <summary>
        /// Concatenates multiple audio files into a single PCM buffer.
        /// Files are decoded to float PCM at the sample rate of the first file.
        /// </summary>
        public static Task<ConcatenatedAudio?> ConcatenateAsync(IEnumerable<string> filePaths)
        {
            return Task.Run(() => Concatenate(filePaths));
        }

        private static ConcatenatedAudio? Concatenate(IEnumerable<string> filePaths)
        {
            var paths = filePaths.ToList();
            if (paths.Count < 2) return null;

            int targetRate = 0;
            int targetChannels = 0;
            var allSamples = new List<float>();

            foreach (var path in paths)
            {
                int stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
                if (stream == 0) continue;

                try
                {
                    var info = Bass.ChannelGetInfo(stream);

                    if (targetRate == 0)
                    {
                        targetRate = info.Frequency;
                        targetChannels = info.Channels;
                    }

                    // Read all PCM data from this file
                    int chunkSize = 65536;
                    float[] buffer = new float[chunkSize];

                    while (true)
                    {
                        int got = Bass.ChannelGetData(stream, buffer, chunkSize * sizeof(float));
                        if (got <= 0) break;
                        int samplesRead = got / sizeof(float);

                        // If channel count differs, convert
                        if (info.Channels == targetChannels)
                        {
                            for (int i = 0; i < samplesRead; i++)
                                allSamples.Add(buffer[i]);
                        }
                        else if (info.Channels == 1 && targetChannels == 2)
                        {
                            // Mono → Stereo
                            for (int i = 0; i < samplesRead; i++)
                            {
                                allSamples.Add(buffer[i]);
                                allSamples.Add(buffer[i]);
                            }
                        }
                        else if (info.Channels == 2 && targetChannels == 1)
                        {
                            // Stereo → Mono
                            for (int i = 0; i < samplesRead - 1; i += 2)
                                allSamples.Add((buffer[i] + buffer[i + 1]) / 2f);
                        }
                        else
                        {
                            // Just take first N channels
                            int min = Math.Min(info.Channels, targetChannels);
                            for (int i = 0; i < samplesRead; i += info.Channels)
                                for (int c = 0; c < min; c++)
                                    allSamples.Add(buffer[i + c]);
                        }
                    }
                }
                finally
                {
                    Bass.StreamFree(stream);
                }
            }

            if (allSamples.Count == 0 || targetRate == 0) return null;

            return new ConcatenatedAudio(allSamples.ToArray(), targetRate, targetChannels, paths);
        }

        /// <summary>
        /// Exports concatenated audio to MP3 using LAME via ManagedBass.Enc.
        /// </summary>
        public static Task<bool> ExportToMp3Async(ConcatenatedAudio audio, string outputPath)
        {
            return Task.Run(() => ExportToMp3(audio, outputPath));
        }

        private static bool ExportToMp3(ConcatenatedAudio audio, string outputPath)
        {
            // Convert float PCM [-1,1] to 16-bit PCM for LAME compatibility
            int totalSamples = audio.PcmData.Length;
            short[] pcm16 = new short[totalSamples];
            for (int i = 0; i < totalSamples; i++)
            {
                float v = Math.Clamp(audio.PcmData[i], -1f, 1f);
                pcm16[i] = (short)(v * 32767);
            }

            // Write a temporary WAV file, then encode with LAME
            string tempWav = Path.Combine(Path.GetTempPath(), $"radiologger_concat_{Guid.NewGuid():N}.wav");

            try
            {
                // Write WAV
                using (var fs = new FileStream(tempWav, FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    int dataBytes = pcm16.Length * 2;
                    int bitsPerSample = 16;
                    int blockAlign = audio.Channels * (bitsPerSample / 8);
                    int byteRate = audio.SampleRate * blockAlign;

                    // RIFF header
                    bw.Write(new[] { 'R', 'I', 'F', 'F' });
                    bw.Write(36 + dataBytes);
                    bw.Write(new[] { 'W', 'A', 'V', 'E' });

                    // fmt chunk
                    bw.Write(new[] { 'f', 'm', 't', ' ' });
                    bw.Write(16); // chunk size
                    bw.Write((short)1); // PCM format
                    bw.Write((short)audio.Channels);
                    bw.Write(audio.SampleRate);
                    bw.Write(byteRate);
                    bw.Write((short)blockAlign);
                    bw.Write((short)bitsPerSample);

                    // data chunk
                    bw.Write(new[] { 'd', 'a', 't', 'a' });
                    bw.Write(dataBytes);

                    byte[] buf = new byte[pcm16.Length * 2];
                    Buffer.BlockCopy(pcm16, 0, buf, 0, buf.Length);
                    bw.Write(buf);
                }

                // Encode WAV → MP3 with LAME
                string lameExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = lameExe,
                    Arguments = $"-b 192 \"{tempWav}\" \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return false;

                // Read stdout/stderr to avoid deadlock, then wait
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                return proc.ExitCode == 0 && File.Exists(outputPath);
            }
            finally
            {
                try { File.Delete(tempWav); } catch { }
            }
        }
    }
}
