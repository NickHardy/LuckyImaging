using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NINA.Luckyimaging.Sequencer.Utility {
    /// <summary>
    /// Writes SER (v3) files:
    /// - 178 byte header (includes all offsets)
    /// - raw 16‑bit LE pixel frames
    /// - 8‑byte FILETIME trailer per frame
    /// </summary>
    public class SerWriter : IDisposable {
        private const int HEADER_SIZE = 178; // 178 ser v2 format
        private const string SIGNATURE = "LUCAM-RECORDER"; // 14 bytes + 0‑pad
        private const uint LU_ID = 0;
        private const uint COLOR_ID = 0;   // 0 = MONO
        private const uint LITTLE_ENDIAN = 1;   // 1 = LE pixel data
        private const uint BIG_ENDIAN = 0;   // 0 = BE pixel data
        private const uint BITS_PER_PIXEL = 16;  // pixel depth
        private const uint TIMESTAMP_TYPE = 2;   // 2 = 64‑bit FILETIME

        private readonly FileStream _fs;
        private readonly BinaryWriter _bw;
        private readonly int _width, _height;
        private readonly DateTime _startLocal, _startUtc;
        private readonly List<long> _timestamps = new();
        private int _frameCount = 0;
        private bool _closed = false;

        public SerWriter(string filePath, int width, int height) {
            _width = width;
            _height = height;
            _startLocal = DateTime.Now;
            _startUtc = DateTime.UtcNow;

            _fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            _bw = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);

            // reserve header space
            _bw.Write(new byte[HEADER_SIZE]);
        }

        public void AddFrame(ushort[] imageData, DateTime frameTime) {
            if (imageData.Length != _width * _height)
                throw new ArgumentException($"Expected {_width * _height} pixels, got {imageData.Length}");

            foreach (ushort px in imageData) {
                // Tangra can't read little endian, but all players can read bigendian, so default to that.
                //ushort swapped = (ushort)((px >> 8) | (px << 8)); // swap bytes big endian -> little endian
                _bw.Write(px);
            }

            long ft = frameTime.ToUniversalTime().ToFileTimeUtc();
            _timestamps.Add(ft);
            _frameCount++;
        }

        public void Close() {
            if (_closed) return;
            _closed = true;

            _bw.Flush();

            // compute per-frame size
            uint bytesPerFrame = (uint)(_width * _height * (BITS_PER_PIXEL / 8));

            // --- 1) Write the timestamp trailer ---
            long trailerPos = HEADER_SIZE + (long)bytesPerFrame * _frameCount;
            _fs.Seek(trailerPos, SeekOrigin.Begin);

            using (var tsW = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true)) {
                foreach (long tick in _timestamps)
                    tsW.Write(tick);
            }

            // --- 2) Rewrite the 178 byte header ---
            WriteHeader(bytesPerFrame);

            _bw.Close();
            _fs.Close();

            Logger.Debug($"SER: wrote {_frameCount} frames, timestamps: {JsonConvert.SerializeObject(_timestamps)}");
        }

        private void WriteHeader(uint bytesPerFrame) {
            using var ms = new MemoryStream(HEADER_SIZE);
            using var hw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // signature (14 bytes + pad)
            hw.Write(Encoding.ASCII.GetBytes(SIGNATURE.PadRight(14, '\0')));
            // LuID, ColorID, Endianness
            hw.Write(LU_ID);
            hw.Write(COLOR_ID);
            hw.Write(BIG_ENDIAN);
            // Width, Height, BitsPerPixel, FrameCount
            hw.Write((uint)_width);
            hw.Write((uint)_height);
            hw.Write(BITS_PER_PIXEL);
            hw.Write((uint)_frameCount);
            // Observer/Instrument/Telescope (3×40 bytes)
            hw.Write(new byte[40]);
            hw.Write(new byte[40]);
            hw.Write(new byte[40]);
            // Start times: local, then UTC
            hw.Write((ulong)_startLocal.ToFileTime());
            hw.Write((ulong)_startUtc.ToFileTime());

            // do not write v3 headers, most players can't read it.
            //// Offsets & lengths
            //uint frameDataOffset = HEADER_SIZE;
            //uint totalFrameBytes = bytesPerFrame * (uint)_frameCount;
            //uint timestampBytes = (uint)(_frameCount * 8);
            //uint timestampOffset = frameDataOffset + totalFrameBytes;
            //uint trailerOffset = 0;

            //hw.Write(frameDataOffset);
            //hw.Write(totalFrameBytes);
            //hw.Write(timestampBytes);
            //hw.Write(timestampOffset);
            //hw.Write(trailerOffset);
            //hw.Write(TIMESTAMP_TYPE);

            // pad to 178
            int pad = HEADER_SIZE - (int)ms.Position;
            if (pad > 0)
                hw.Write(new byte[pad]);

            // write back
            _fs.Seek(0, SeekOrigin.Begin);
            _fs.Write(ms.GetBuffer(), 0, HEADER_SIZE);
        }

        public void Dispose() => Close();
    }
}
