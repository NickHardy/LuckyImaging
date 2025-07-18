using NINA.Core.Utility;
using NINA.Image.Interfaces;
using System;
using System.IO;

namespace NINA.Luckyimaging.Sequencer.Utility;

public class FitsCubeWriter {
    private readonly int width;
    private readonly int height;
    private readonly int frameCount;
    private readonly string filePath;
    private readonly FileStream stream;
    private readonly BinaryWriter writer;

    public FitsCubeWriter(string filePath, int width, int height, IImageData imageData, int frameCount) {
        this.filePath = filePath;
        this.width = width;
        this.height = height;
        this.frameCount = frameCount;
        var header = new FITSHeader(width, height, frameCount);
        header.PopulateFromMetaData(imageData.MetaData);

        stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        writer = new BinaryWriter(stream);

        header.Write(stream);
    }

    public void AddFrame(ushort[] frame) {
        if (frame.Length != width * height) {
            Logger.Error($"Frame length {frame.Length} does not match file. Width: {width} Height: {height} Length: {width * height}");
            throw new ArgumentException("Image size doesn't match.");
        }

        /* Write image data */
        for (int i = 0; i < frame.Length; i++) {
            var val = (short)(frame[i] - (short.MaxValue + 1));
            stream.WriteByte((byte)(val >> 8));
            stream.WriteByte((byte)val);
        }
    }

    public void Close() {
        PadData();
        writer.Close();
        stream.Close();
    }

    private void PadData() {
        long remainingBlockPadding = (long)Math.Ceiling((double)stream.Position / (double)BLOCKSIZE) * (long)BLOCKSIZE - stream.Position;
        byte zeroByte = 0;
        //Pad remaining FITS block with zero values
        for (int i = 0; i < remainingBlockPadding; i++) {
            stream.WriteByte(zeroByte);
        }
    }

    /* Header card size Specification: http://archive.stsci.edu/fits/fits_standard/node29.html#SECTION00912100000000000000 */
    public const int HEADERCARDSIZE = 80;
    /* Blocksize specification: http://archive.stsci.edu/fits/fits_standard/node13.html#SECTION00810000000000000000 */
    public const int BLOCKSIZE = 2880;
    public const int BITPIX_BYTE = 8;
    public const int BITPIX_SHORT = 16;
    public const int BITPIX_INT = 32;
    public const int BITPIX_LONG = 64;
    public const int BITPIX_FLOAT = -32;
    public const int BITPIX_DOUBLE = -64;

}
