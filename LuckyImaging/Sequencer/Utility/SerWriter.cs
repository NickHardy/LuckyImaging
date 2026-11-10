using Newtonsoft.Json;
using NINA.Core.Enum;
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
    /// - 8‑byte time stamp trailer per frame
    /// Time stamps and the header dates are .NET DateTime ticks (100 ns since 0001-01-01), as the SER format defines them
    /// and as Siril, PIPP, AutoStakkert, FireCapture and SharpCap read and write them. The trailer and DateTime_UTC are UTC;
    /// the header DateTime is local time. (Windows FILETIME counts from 1601, so readers showed those files as year 0426.)
    /// </summary>
    public class SerWriter : IDisposable {
        private const int HEADER_SIZE = 178; // 178 ser v2 format
        private const string SIGNATURE = "LUCAM-RECORDER"; // 14 bytes + 0‑pad
        private const uint LU_ID = 0;
        public const uint COLOR_ID_MONO = 0;
        public const uint COLOR_ID_BAYER_RGGB = 8;
        public const uint COLOR_ID_BAYER_GRBG = 9;
        public const uint COLOR_ID_BAYER_GBRG = 10;
        public const uint COLOR_ID_BAYER_BGGR = 11;
        private const uint LITTLE_ENDIAN = 1;   // 1 = LE pixel data
        private const uint BIG_ENDIAN = 0;   // 0 = BE pixel data
        private const uint BITS_PER_PIXEL = 16;  // pixel depth

        private readonly FileStream _fs;
        private readonly BinaryWriter _bw;
        private readonly int _width, _height;
        private readonly uint _colorId;
        private readonly DateTime _createdUtc;
        private readonly List<long> _timestamps = new();
        private int _frameCount = 0;
        private bool _closed = false;

        public SerWriter(string filePath, int width, int height, uint colorId = COLOR_ID_MONO) {
            _width = width;
            _height = height;
            _colorId = colorId;
            _createdUtc = DateTime.UtcNow;

            _fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            _bw = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);

            // reserve header space
            _bw.Write(new byte[HEADER_SIZE]);
        }

        /// <summary>
        /// SER ColorID for the frames NINA delivers, using the sensor type and Bayer offsets of the image meta data
        /// (which already apply the profile's Bayer pattern setting) and the same offset rule NINA uses to debayer.
        /// Patterns SER cannot describe are written as MONO.
        /// </summary>
        public static uint ColorIdFor(SensorType sensorType, int bayerOffsetX, int bayerOffsetY) {
            var pattern = sensorType;
            if (bayerOffsetX % 2 != 0) {
                pattern = pattern switch {
                    SensorType.RGGB => SensorType.GRBG,
                    SensorType.GRBG => SensorType.RGGB,
                    SensorType.GBRG => SensorType.BGGR,
                    SensorType.BGGR => SensorType.GBRG,
                    _ => pattern
                };
            }
            if (bayerOffsetY % 2 != 0) {
                pattern = pattern switch {
                    SensorType.RGGB => SensorType.GBRG,
                    SensorType.GBRG => SensorType.RGGB,
                    SensorType.GRBG => SensorType.BGGR,
                    SensorType.BGGR => SensorType.GRBG,
                    _ => pattern
                };
            }
            return pattern switch {
                SensorType.RGGB => COLOR_ID_BAYER_RGGB,
                SensorType.GRBG => COLOR_ID_BAYER_GRBG,
                SensorType.GBRG => COLOR_ID_BAYER_GBRG,
                SensorType.BGGR => COLOR_ID_BAYER_BGGR,
                _ => COLOR_ID_MONO
            };
        }

        public void AddFrame(ushort[] imageData, DateTime frameTime) {
            if (imageData.Length != _width * _height)
                throw new ArgumentException($"Expected {_width * _height} pixels, got {imageData.Length}");

            foreach (ushort px in imageData) {
                // Tangra can't read little endian, but all players can read bigendian, so default to that.
                //ushort swapped = (ushort)((px >> 8) | (px << 8)); // swap bytes big endian -> little endian
                _bw.Write(px);
            }

            _timestamps.Add(frameTime.ToUniversalTime().Ticks);
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

            Logger.Debug($"SER: wrote {_frameCount} frames, ColorID {_colorId}, timestamps: {JsonConvert.SerializeObject(_timestamps)}");
        }

        private void WriteHeader(uint bytesPerFrame) {
            using var ms = new MemoryStream(HEADER_SIZE);
            using var hw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // signature (14 bytes + pad)
            hw.Write(Encoding.ASCII.GetBytes(SIGNATURE.PadRight(14, '\0')));
            // LuID, ColorID, Endianness
            hw.Write(LU_ID);
            hw.Write(_colorId);
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
            // Start of the video (the first frame's time stamp; the file's creation if it has no frames): local, then UTC
            var startUtc = _timestamps.Count > 0 ? new DateTime(_timestamps[0], DateTimeKind.Utc) : _createdUtc;
            hw.Write(startUtc.ToLocalTime().Ticks);
            hw.Write(startUtc.Ticks);

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
