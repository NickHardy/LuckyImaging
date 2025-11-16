#region "copyright"

/*
    Copyright © 2016 - 2024 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Luckyimaging.Sequencer.Utility; 

public class ImageStatistics : BaseINPC, IImageStatistics {

    private ImageStatistics() {
    }

    public int BitDepth { get; private set; }
    public double StDev { get; private set; }
    public double Mean { get; private set; }
    public double Median { get; private set; }
    public double MedianAbsoluteDeviation { get; private set; }
    public int Max { get; private set; }
    public long MaxOccurrences { get; private set; }
    public int Min { get; private set; }
    public long MinOccurrences { get; private set; }
    public ImmutableList<OxyPlot.DataPoint> Histogram { get; private set; }
    public bool ObjectFound { get; private set; }
    public double ObjectCenterX { get; private set; }
    public double ObjectCenterY { get; private set; }

    public static IImageStatistics Create(IImageData imageData) {
        return Create(imageData.Properties, imageData.Data.FlatArray);
    }

    public static IImageStatistics CreateFast(IImageData imageData) {
        return CreateFast(imageData.Properties, imageData.Data.FlatArray);
    }

    public static IImageStatistics Create(ImageProperties imageProperties, ushort[] array) {
        using (MyStopWatch.Measure()) {
            long sum = 0;
            long squareSum = 0;
            int count = array.Length;
            ushort min = ushort.MaxValue;
            ushort oldmin = min;
            ushort max = 0;
            ushort oldmax = max;
            long maxOccurrences = 0;
            long minOccurrences = 0;

            /* Array mapping: pixel value -> total number of occurrences of that pixel value */
            int[] pixelValueCounts = new int[ushort.MaxValue + 1];
            for (var i = 0; i < array.Length; i++) {
                ushort val = array[i];

                sum += val;
                squareSum += (long)val * val;

                pixelValueCounts[val]++;

                min = Math.Min(min, val);
                if (min != oldmin) {
                    minOccurrences = 0;
                }
                if (val == min) {
                    minOccurrences += 1;
                }

                max = Math.Max(max, val);
                if (max != oldmax) {
                    maxOccurrences = 0;
                }
                if (val == max) {
                    maxOccurrences += 1;
                }

                oldmin = min;
                oldmax = max;
            }

            double mean = sum / (double)count;
            double variance = (squareSum - count * mean * mean) / (count);
            double stdev = Math.Sqrt(variance);

            var occurrences = 0;
            double median = 0d;
            int median1 = 0, median2 = 0;
            var medianlength = array.Length / 2.0;

            /* Determine median out of histogram array */
            for (ushort i = 0; i < ushort.MaxValue; i++) {
                occurrences += pixelValueCounts[i];
                if (occurrences > medianlength) {
                    median1 = i;
                    median2 = i;
                    break;
                } else if (occurrences == medianlength) {
                    median1 = i;
                    for (int j = i + 1; j <= ushort.MaxValue; j++) {
                        if (pixelValueCounts[j] > 0) {
                            median2 = j;
                            break;
                        }
                    }
                    break;
                }
            }
            median = (median1 + median2) / 2.0;

            /* Determine median Absolute Deviation out of histogram array and previously determined median
             * As the histogram already has the values sorted and we know the median,
             * we can determine the mad by beginning from the median and step up and down
             * By doing so we will gain a sorted list automatically, because MAD = DetermineMedian(|xn - median|)
             * So starting from the median will be 0 (as median - median = 0), going up and down will increment by the steps
             */

            var medianAbsoluteDeviation = 0.0d;
            occurrences = 0;
            var idxDown = median1;
            var idxUp = median2;
            while (true) {
                if (idxDown >= 0 && idxDown != idxUp) {
                    occurrences += pixelValueCounts[idxDown] + pixelValueCounts[idxUp];
                } else {
                    occurrences += pixelValueCounts[idxUp];
                }

                if (occurrences > medianlength) {
                    medianAbsoluteDeviation = Math.Abs(idxUp - median);
                    break;
                }

                idxUp++;
                idxDown--;
                if (idxUp > ushort.MaxValue) {
                    break;
                }
            }

            var statistics = new ImageStatistics();
            statistics.BitDepth = imageProperties.BitDepth;
            statistics.StDev = stdev;
            statistics.Mean = mean;
            statistics.Median = median;
            statistics.MedianAbsoluteDeviation = medianAbsoluteDeviation;
            statistics.Max = max;
            statistics.MaxOccurrences = maxOccurrences;
            statistics.Min = min;
            statistics.MinOccurrences = minOccurrences;
            statistics.Histogram = null;
            return statistics;
        }
    }

    public static IImageStatistics CreateFast(ImageProperties props, ushort[] pixels) {
        int bucketCount = ushort.MaxValue + 1;
        int processorCount = Environment.ProcessorCount;

        // Thread‑local accumulators
        var localSums = new long[processorCount];
        var localSquareSums = new long[processorCount];
        var localHists = new int[processorCount][];

        for (int t = 0; t < processorCount; t++)
            localHists[t] = new int[bucketCount];

        // Partition the work
        var rangeSize = (pixels.Length + processorCount - 1) / processorCount;
        Parallel.For(0, processorCount, t =>
        {
            int start = t * rangeSize;
            int end = Math.Min(start + rangeSize, pixels.Length);
            long sum = 0, sq = 0;
            var hist = localHists[t];

            for (int i = start; i < end; i++) {
                ushort v = pixels[i];
                sum += v;
                sq += (long)v * v;
                hist[v]++;
            }

            localSums[t] = sum;
            localSquareSums[t] = sq;
        });

        // Merge thread‑local results
        long totalSum = 0, totalSquareSum = 0;
        var histogram = new int[bucketCount];
        for (int t = 0; t < processorCount; t++) {
            totalSum += localSums[t];
            totalSquareSum += localSquareSums[t];

            var hist = localHists[t];
            for (int i = 0; i < bucketCount; i++)
                histogram[i] += hist[i];
        }

        int count = pixels.Length;
        double mean = totalSum / (double)count;
        double variance = (totalSquareSum - count * mean * mean) / count;
        double stDev = Math.Sqrt(variance);

        // Find min, max and their occurrences
        int min = Array.FindIndex(histogram, c => c > 0);
        int max = Array.FindLastIndex(histogram, c => c > 0);
        long minOcc = histogram[min];
        long maxOcc = histogram[max];

        // Compute median from the histogram in O(bucketCount)
        long cum = 0;
        int median1 = 0, median2 = 0;
        double halfCount = count / 2.0;
        for (int i = 0; i < bucketCount; i++) {
            cum += histogram[i];
            if (cum > halfCount) {
                median1 = median2 = i;
                break;
            } else if (cum == halfCount) {
                median1 = i;
                // find next non‑zero bucket
                for (int j = i + 1; ; j++)
                    if (histogram[j] > 0) {
                        median2 = j;
                        break;
                    }
                break;
            }
        }
        double median = (median1 + median2) / 2.0;

        // Compute Median Absolute Deviation via a second small histogram:
        var diffHist = new int[bucketCount];
        for (int i = 0; i < bucketCount; i++) {
            if (histogram[i] == 0) continue;
            int d = Math.Abs(i - median1);
            diffHist[d] += histogram[i];
        }
        cum = 0;
        int mad1 = 0, mad2 = 0;
        for (int d = 0; d < bucketCount; d++) {
            cum += diffHist[d];
            if (cum > halfCount) {
                mad1 = mad2 = d;
                break;
            } else if (cum == halfCount) {
                mad1 = d;
                // next d+1 will be the next non‑zero
                for (int e = d + 1; ; e++)
                    if (diffHist[e] > 0) {
                        mad2 = e;
                        break;
                    }
                break;
            }
        }
        double mad = (mad1 + mad2) / 2.0;

        return new ImageStatistics {
            BitDepth = props.BitDepth,
            Mean = mean,
            StDev = stDev,
            Median = median,
            MedianAbsoluteDeviation = mad,
            Min = min,
            MinOccurrences = minOcc,
            Max = max,
            MaxOccurrences = maxOcc,
            Histogram = null
        };
    }

    public static ImageAnalysisResult CreateFast(
        IImageData imageData,
        int cameraWidth,
        int cameraHeight,
        int targetPixelThreshold,
        ObservableRectangle seedRect) {
        // Extract dimensions from props
        int width = imageData.Properties.Width;
        int height = imageData.Properties.Height;
        var pixels = imageData.Data.FlatArray;
        int totalPixels = pixels.Length;
        if (width * height != totalPixels)
            throw new ArgumentException("Pixel array length does not match image dimensions.");

        int bucketCount = ushort.MaxValue + 1;
        int processorCount = Environment.ProcessorCount;

        // Thread-local accumulators for global stats
        var localSums = new long[processorCount];
        var localSquareSums = new long[processorCount];
        var localHists = new int[processorCount][];

        // Thread-local accumulators for object centroid
        var localCount = new long[processorCount];
        var localSumX = new long[processorCount];
        var localSumY = new long[processorCount];

        for (int t = 0; t < processorCount; t++)
            localHists[t] = new int[bucketCount];

        int rangeSize = (totalPixels + processorCount - 1) / processorCount;
        Parallel.For(0, processorCount, t =>
        {
            int start = t * rangeSize;
            int end = Math.Min(start + rangeSize, totalPixels);
            long sum = 0, sq = 0;
            long count = 0, sumX = 0, sumY = 0;
            var hist = localHists[t];

            for (int i = start; i < end; i++) {
                ushort v = pixels[i];
                sum += v;
                sq += (long)v * v;
                hist[v]++;

                if (v > targetPixelThreshold) {
                    int y = i / width;
                    int x = i - (y * width);
                    sumX += x;
                    sumY += y;
                    count++;
                }
            }

            localSums[t] = sum;
            localSquareSums[t] = sq;
            localCount[t] = count;
            localSumX[t] = sumX;
            localSumY[t] = sumY;
        });

        // Merge thread-local results
        long totalSum = 0, totalSquareSum = 0;
        var histogram = new int[bucketCount];
        long objCount = 0, objSumX = 0, objSumY = 0;
        for (int t = 0; t < processorCount; t++) {
            totalSum += localSums[t];
            totalSquareSum += localSquareSums[t];
            objCount += localCount[t];
            objSumX += localSumX[t];
            objSumY += localSumY[t];

            var hist = localHists[t];
            for (int i = 0; i < bucketCount; i++)
                histogram[i] += hist[i];
        }

        int countPixels = totalPixels;
        double mean = totalSum / (double)countPixels;
        double variance = (totalSquareSum - countPixels * mean * mean) / countPixels;
        double stDev = Math.Sqrt(variance);

        // Min/max
        int min = Array.FindIndex(histogram, c => c > 0);
        int max = Array.FindLastIndex(histogram, c => c > 0);
        long minOcc = histogram[min];
        long maxOcc = histogram[max];

        // Median
        long cum = 0;
        int median1 = 0, median2 = 0;
        double halfCount = countPixels / 2.0;
        for (int i = 0; i < bucketCount; i++) {
            cum += histogram[i];
            if (cum > halfCount) { median1 = median2 = i; break; }
            if (cum == halfCount) {
                median1 = i;
                for (int j = i + 1; ; j++) if (histogram[j] > 0) { median2 = j; break; }
                break;
            }
        }
        double median = (median1 + median2) / 2.0;

        // Median Absolute Deviation
        var diffHist = new int[bucketCount];
        for (int i = 0; i < bucketCount; i++) {
            if (histogram[i] == 0) continue;
            int d = Math.Abs(i - median1);
            diffHist[d] += histogram[i];
        }
        cum = 0;
        int mad1 = 0, mad2 = 0;
        for (int d = 0; d < bucketCount; d++) {
            cum += diffHist[d];
            if (cum > halfCount) { mad1 = mad2 = d; break; }
            if (cum == halfCount) {
                mad1 = d;
                for (int e = d + 1; ; e++) if (diffHist[e] > 0) { mad2 = e; break; }
                break;
            }
        }
        double mad = (mad1 + mad2) / 2.0;

        var stats = new ImageStatistics {
            BitDepth = imageData.Properties.BitDepth,
            Mean = mean,
            StDev = stDev,
            Median = median,
            MedianAbsoluteDeviation = mad,
            Min = min,
            MinOccurrences = minOcc,
            Max = max,
            MaxOccurrences = maxOcc,
            Histogram = null
        };

        // --- Build ROI from the merged sums ---
        ObservableRectangle roi;
        bool objectFound = objCount > 0;
        if (objectFound) {
            int centerX = (int)(objectFound ? objSumX / (double)objCount : 0);
            int centerY = (int)(objectFound ? objSumY / (double)objCount : 0);

            // if full‑image vs camera ROI mismatch, offset by seedRect
            if (width != cameraWidth || height != cameraHeight) {
                centerX += (int)seedRect.X;
                centerY += (int)seedRect.Y;
            }

            // clamp into camera bounds
            double halfW = seedRect.Width / 2;
            double halfH = seedRect.Height / 2;

            double x0 = Math.Min(Math.Max(centerX - halfW, 0), cameraWidth);
            double y0 = Math.Min(Math.Max(centerY - halfH, 0), cameraHeight);

            roi = new ObservableRectangle {
                X = x0,
                Y = y0,
                Width = seedRect.Width,
                Height = seedRect.Height
            };
        } else {
            // fallback
            roi = new ObservableRectangle {
                X = 0,
                Y = 0,
                Width = cameraWidth,
                Height = cameraHeight
            };
        }

        return new ImageAnalysisResult {
            Statistics = stats,
            Roi = roi
        };
    }


    public class ImageAnalysisResult {
        public ImageStatistics Statistics { get; set; }
        public ObservableRectangle Roi { get; set; }
    }



}