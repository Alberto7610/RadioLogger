using ManagedBass;
using ManagedBass.Enc;
using Serilog;
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
        private static readonly ILogger _log = AppLog.For(typeof(AudioConcatenator).FullName!);

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
        /// Exports concatenated audio to MP3.
        /// Uses bassenc_mp3.dll (libmp3lame in-process) — no temporary WAV, no external process.
        /// Falls back to lame.exe via stdin pipe if the native DLL is not available (still no temp WAV).
        /// </summary>
        public static Task<bool> ExportToMp3Async(ConcatenatedAudio audio, string outputPath)
        {
            return Task.Run(() => ExportToMp3(audio, outputPath));
        }

        private static bool ExportToMp3(ConcatenatedAudio audio, string outputPath)
        {
            const string options = "-b 192";
            int stream = 0;
            int encoder = 0;

            try
            {
                // 1. Create a push decode stream backed by the in-memory float PCM
                stream = Bass.CreateStream(
                    audio.SampleRate,
                    audio.Channels,
                    BassFlags.Float | BassFlags.Decode,
                    StreamProcedureType.Push);

                if (stream == 0)
                {
                    _log.Error("ExportToMp3: no se pudo crear stream push ({Error}) para {Path}",
                        Bass.LastError, outputPath);
                    return false;
                }

                // 2. Push the entire PCM buffer and signal end-of-stream
                var bytes = new byte[audio.PcmData.Length * sizeof(float)];
                Buffer.BlockCopy(audio.PcmData, 0, bytes, 0, bytes.Length);

                if (Bass.StreamPutData(stream, bytes, bytes.Length) < 0)
                {
                    _log.Error("ExportToMp3: StreamPutData falló ({Error}) para {Path}",
                        Bass.LastError, outputPath);
                    return false;
                }
                Bass.StreamPutData(stream, IntPtr.Zero, (int)StreamProcedureType.End);

                // 3. Try in-process MP3 encoder first (bassenc_mp3.dll)
                bool usedFallback = false;
                try
                {
                    encoder = BassEnc_Mp3.Start(stream, options, EncodeFlags.Default, outputPath);
                }
                catch (DllNotFoundException)
                {
                    encoder = 0;
                }

                // 4. Fallback: external lame.exe via stdin pipe (no temp WAV)
                if (encoder == 0)
                {
                    _log.Warning("ExportToMp3: BassEnc_Mp3 no disponible ({Error}), usando lame.exe fallback",
                        Bass.LastError);

                    string lamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lame.exe");
                    if (!File.Exists(lamePath))
                    {
                        _log.Error("ExportToMp3: lame.exe no encontrado en {Path} — exportación abortada", lamePath);
                        return false;
                    }

                    string command = $"\"{lamePath}\" {options} - \"{outputPath}\"";
                    encoder = BassEnc.EncodeStart(stream, command, EncodeFlags.Default, null, IntPtr.Zero);

                    if (encoder == 0)
                    {
                        _log.Error("ExportToMp3: lame.exe fallback también falló ({Error}) para {Path}",
                            Bass.LastError, outputPath);
                        return false;
                    }
                    usedFallback = true;
                }

                // 5. Drain the decode stream to feed the encoder
                byte[] drainBuf = new byte[65536];
                long totalDrained = 0;
                while (true)
                {
                    int got = Bass.ChannelGetData(stream, drainBuf, drainBuf.Length);
                    if (got <= 0) break;
                    totalDrained += got;
                }

                // 6. Stop encoder cleanly so the MP3 file gets finalized (especially the lame.exe fallback)
                BassEnc.EncodeStop(encoder);
                encoder = 0;

                if (!File.Exists(outputPath))
                {
                    _log.Error("ExportToMp3: encoder finalizó pero el archivo no existe: {Path}", outputPath);
                    return false;
                }

                long fileBytes = new FileInfo(outputPath).Length;
                if (fileBytes == 0)
                {
                    _log.Error("ExportToMp3: archivo de salida vacío (0 bytes): {Path}", outputPath);
                    File.Delete(outputPath);
                    return false;
                }

                _log.Information(
                    "ExportToMp3: MP3 exportado a {Path} ({FileBytes:N0} B, {DrainedBytes:N0} B PCM, {Encoder})",
                    outputPath, fileBytes, totalDrained, usedFallback ? "lame.exe fallback" : "bassenc_mp3 nativo");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "ExportToMp3: excepción durante exportación a {Path}", outputPath);
                return false;
            }
            finally
            {
                if (encoder != 0)
                {
                    try { BassEnc.EncodeStop(encoder); } catch { }
                }
                if (stream != 0)
                {
                    try { Bass.StreamFree(stream); } catch { }
                }
            }
        }
    }
}
