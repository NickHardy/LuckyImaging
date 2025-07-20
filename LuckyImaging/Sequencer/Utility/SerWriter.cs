using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NINA.Luckyimaging.Sequencer.Utility;

public class SerWriter : IDisposable {
    private readonly FileStream _fs;
    private readonly BinaryWriter _bw;
    private readonly int _width, _height;
    private int _frameCount;
    private readonly DateTime _startLocal, _startUtc;
    private readonly List<long> _timestamps = new();
    private bool _closed = false;

    public SerWriter(string filePath, int width, int height) {
        _width = width;
        _height = height;
        _frameCount = 0;
        _startLocal = DateTime.Now;
        _startUtc = DateTime.UtcNow;

        _fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        _bw = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);

        // Reserve header space
        _bw.Write(new byte[178]);
    }

    public void AddFrame(ushort[] imageData, DateTime frameTime) {
        if (imageData.Length != _width * _height)
            throw new ArgumentException($"Expected {_width * _height} pixels, got {imageData.Length}");

        // Write raw pixels (little‑endian)
        foreach (ushort px in imageData)
            _bw.Write(px);

        // Record just the FILETIME ticks for the trailer
        long fileTimeUtc = frameTime.ToUniversalTime().ToFileTimeUtc();
        _timestamps.Add(fileTimeUtc);

        // Optional: log elapsed seconds
        double secs = (frameTime.ToUniversalTime() - _startUtc).TotalSeconds;
        Logger.Debug($"Frame {_frameCount} @ +{secs:F3}s  ticks={fileTimeUtc}");

        _frameCount++;
    }

    public void Close() {
        if (_closed) return;
        _closed = true;

        _bw.Flush();

        // --- Write the per‑frame trailer of FILETIMEs ---
        long pixelBytes = (long)_width * _height * 2 * _frameCount;
        long trailerPos = 178 + pixelBytes;
        _fs.Seek(trailerPos, SeekOrigin.Begin);

        using (var tsWriter = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true)) {
            foreach (long ticks in _timestamps)
                tsWriter.Write(ticks);
        }

        // --- Rewrite the header in place ---
        WriteHeader();

        _bw.Close();
        _fs.Close();

        Logger.Debug("Timestamps: " + JsonConvert.SerializeObject(_timestamps));
    }

    private void WriteHeader() {
        using var ms = new MemoryStream(178);
        using var hw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // 1) Signature + IDs
        hw.Write(Encoding.ASCII.GetBytes("LUCAM-RECORDER".PadRight(14, '\0')));
        hw.Write((uint)0);   // LuID
        hw.Write((uint)0);   // ColorID (0=mono)
        hw.Write((uint)1);   // LittleEndian

        // 2) Geometry
        hw.Write((uint)_width);
        hw.Write((uint)_height);
        hw.Write((uint)16);      // bitsPerPixel
        hw.Write((uint)_frameCount);

        // 3) Names (observer, instrument, telescope) – empty for now
        byte[] empty40 = new byte[40];
        hw.Write(empty40);
        hw.Write(empty40);
        hw.Write(empty40);

        // 4) Start times: local then UTC
        hw.Write((ulong)_startLocal.ToFileTimeUtc());
        hw.Write((ulong)_startUtc.ToFileTimeUtc());

        // 5) Pad to 178 bytes
        int pad = 178 - (int)ms.Position;
        if (pad > 0)
            hw.Write(new byte[pad]);

        // Write header back to file
        _fs.Seek(0, SeekOrigin.Begin);
        _fs.Write(ms.GetBuffer(), 0, 178);
    }

    public void Dispose() => Close();
}
