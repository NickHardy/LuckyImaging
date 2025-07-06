using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NINA.Luckyimaging.Sequencer.Utility;

public class SerWriter {
    private FileStream fileStream;
    private BinaryWriter writer;
    private int width, height;
    private int frameCount;
    private string filePath;
    private List<double> secondsList = new();
    private List<long> ticksList = new();
    private DateTime startTime;

    public SerWriter(string filePath, int width, int height) {
        this.filePath = filePath;
        this.width = width;
        this.height = height;
        this.frameCount = 0;
        this.startTime = DateTime.UtcNow;

        fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        writer = new BinaryWriter(fileStream);

        WriteHeaderPlaceholder();
    }

    public void AddFrame(ushort[] imageData, DateTime frameTime) {
        if (imageData.Length != width * height)
            throw new ArgumentException("Image size doesn't match.");

        // Write image data
        foreach (ushort pixel in imageData)
            writer.Write(pixel); // Little-endian ushort

        // Save timestamp
        double secondsSinceStart = (frameTime - startTime).TotalSeconds;
        secondsList.Add(secondsSinceStart);
        ticksList.Add(frameTime.Ticks);

        frameCount++;
    }

    public void Close() {
        WriteTimestamps();
        UpdateHeader();
        writer.Close();
        fileStream.Close();
    }

    private void WriteHeaderPlaceholder() {
        byte[] header = new byte[178];
        writer.Write(header);
    }

    private void UpdateHeader() {
        fileStream.Seek(0, SeekOrigin.Begin);

        uint luId = 0; // unused
        uint colorID = 0; // Mono 16-bit
        uint littleEndian = 1;
        uint bitsPerPixel = 16;
        uint frameDataOffset = 178;
        uint bytesPerFrame = (uint)(width * height * 2);
        uint totalFrameBytes = bytesPerFrame * (uint)frameCount;
        uint timestampBytes = (uint)(frameCount * 16);
        uint timestampOffset = frameDataOffset + totalFrameBytes;

        writer.Write(Encoding.ASCII.GetBytes("LUCAM-RECORDER".PadRight(14, '\0'))); // FileID
        writer.Write(luId);
        writer.Write(colorID);
        writer.Write(littleEndian);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write(bitsPerPixel);
        writer.Write((uint)frameCount);
        writer.Write((uint)0); // ObserverOffset
        writer.Write((uint)0); // InstrumentOffset
        writer.Write((uint)0); // TelescopeOffset
        writer.Write((ulong)startTime.Ticks); // DateTimeUTC
        writer.Write(frameDataOffset);
        writer.Write(totalFrameBytes);
        writer.Write(timestampBytes);
        writer.Write(timestampOffset);
        writer.Write((uint)0); // Trailer offset

        // Fill up to 178 bytes
        int bytesWritten = 76;
        writer.Write(new byte[178 - bytesWritten]);
    }

    private void WriteTimestamps() {
        foreach (var (seconds, ticks) in Zip(secondsList, ticksList)) {
            writer.Write(seconds);
            writer.Write(ticks);
        }
    }

    private IEnumerable<(T1, T2)> Zip<T1, T2>(IList<T1> a, IList<T2> b) {
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            yield return (a[i], b[i]);
    }
}

