using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NINA.Luckyimaging.Sequencer.Utility;

/// <summary>
/// ADV Writer using FSTF TLV header (ADV v2) matching SharpCap/Tangra expectations:
/// - Sections: MAIN, CALIBRATION, IMAGE, STATUS, DATA-LAYOUT, SECTION-DATA-COMPRESSION
/// - Raw 16-bit frames
/// - 8-byte UTC FILETIME timestamps trailer
/// </summary>
public class AdvWriter : IDisposable {
    private const ushort TLV_TYPE = 2;

    private readonly FileStream _fs;
    private readonly BinaryWriter _bw;
    private readonly int _width, _height;
    private readonly DateTime _startUtc;
    private readonly List<ulong> _timestamps = new();
    private int _frameCount;

    // file position of the placeholder frameCount in the MAIN TLV
    private long _mainCountPosition;
    private bool _headerWritten;

    public AdvWriter(string filePath, int width, int height) {
        _width = width;
        _height = height;
        _startUtc = DateTime.UtcNow;
        _frameCount = 0;
        _headerWritten = false;

        _fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        _bw = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);
    }

    public void AddFrame(ushort[] imageData, DateTime frameTime) {
        if (!_headerWritten) {
            WriteHeader();
            _headerWritten = true;
        }

        if (imageData.Length != _width * _height)
            throw new ArgumentException($"Expected {_width * _height} pixels, got {imageData.Length}");

        // Write raw pixel data (little-endian)
        foreach (ushort px in imageData)
            _bw.Write(px);

        // Record timestamp for trailer
        var ticks = (ulong)frameTime.ToUniversalTime().ToFileTimeUtc();
        _timestamps.Add(ticks);
        _frameCount++;
    }

    public void Close() {
        // Update MAIN frameCount
        if (_mainCountPosition > 0) {
            _fs.Seek(_mainCountPosition, SeekOrigin.Begin);
            _bw.Write((uint)_frameCount);
        }

        // Append timestamps trailer
        _fs.Seek(0, SeekOrigin.End);
        foreach (var ts in _timestamps)
            _bw.Write(ts);

        _bw.Flush();
        _bw.Close();
        _fs.Close();

        Logger.Debug($"ADV: Wrote {_frameCount} frames with timestamps");
    }

    private void WriteHeader() {
        // 1) Signature + version
        _bw.Write(Encoding.ASCII.GetBytes("FSTF"));
        _bw.Write((uint)2); // ADV v2

        // 2) MAIN TLV: payload = 8-byte startTime + 4-byte frameCount placeholder
        WriteTlvHeader("MAIN", 12);
        _bw.Write((ulong)_startUtc.ToFileTimeUtc());
        _mainCountPosition = _fs.Position;
        _bw.Write((uint)0);

        // 3) CALIBRATION: empty payload
        WriteTlvHeader("CALIBRATION", 0);

        // 4) IMAGE: width, height, bitsPerPixel, binX, binY, xOffset, yOffset
        byte[] imgPayload;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true)) {
            w.Write((uint)_width);
            w.Write((uint)_height);
            w.Write((ushort)16);  // bits per pixel
            w.Write((ushort)1);   // binX
            w.Write((ushort)1);   // binY
            w.Write((ushort)0);   // xOffset
            w.Write((ushort)0);   // yOffset
            w.Flush();
            imgPayload = ms.ToArray();
        }
        WriteTlvSection("IMAGE", imgPayload);

        // 5) STATUS: empty
        WriteTlvHeader("STATUS", 0);

        // 6) DATA-LAYOUT: "FULL-IMAGE-RAW"
        WriteTlvSection("DATA-LAYOUT", Encoding.ASCII.GetBytes("FULL-IMAGE-RAW"));

        // 7) SECTION-DATA-COMPRESSION: "UNCOMPRESSED"
        WriteTlvSection("SECTION-DATA-COMPRESSION", Encoding.ASCII.GetBytes("UNCOMPRESSED"));
    }

    private void WriteTlvHeader(string name, uint payloadLength) {
        // Type (big-endian)
        _bw.Write((byte)(TLV_TYPE >> 8));
        _bw.Write((byte)(TLV_TYPE & 0xFF));
        // Name length (little-endian)
        _bw.Write((ushort)name.Length);
        // Name ASCII
        _bw.Write(Encoding.ASCII.GetBytes(name));
        // Payload length (little-endian)
        _bw.Write(payloadLength);
    }

    private void WriteTlvSection(string name, byte[] payload) {
        WriteTlvHeader(name, (uint)payload.Length);
        if (payload.Length > 0)
            _bw.Write(payload);
    }

    public void Dispose() => Close();
}
