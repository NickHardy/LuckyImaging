#region "copyright"

/*
    Copyright © 2016 - 2022 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Validations;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.ViewModel.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Core.Model.Equipment;
using NINA.Core.Locale;
using NINA.Equipment.Model;
using NINA.Astrometry;
using NINA.Equipment.Equipment.MyCamera;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Sequencer.Interfaces;
using Dasync.Collections;
using System.Diagnostics;
using NINA.Image.FileFormat;
using NINA.Sequencer.SequenceItem;
using NINA.Luckyimaging.Sequencer.Utility;
using NINA.Image.ImageData;
using NINA.Luckyimaging.Sequencer.Container;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Utility;
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using NINA.Image.Interfaces;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Reflection;

namespace NINA.Luckyimaging.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Take Video Roi Exposures")]
    [ExportMetadata("Description", "Currently only QHY, ZWO and Touptek are supported for video imaging.")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "LuckyImaging")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TakeLiveExposures : NINA.Sequencer.SequenceItem.SequenceItem, IExposureItem, IValidatable, ICameraConsumer, ITelescopeConsumer, IFilterWheelConsumer, IFocuserConsumer, IRotatorConsumer, IWeatherDataConsumer {
        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;
        private IImageSaveMediator imageSaveMediator;
        private IImageHistoryVM imageHistoryVM;
        private IProfileService profileService;
        private IFilterWheelMediator filterWheelMediator;
        private FilterWheelInfo filterWheelInfo;
        private IFocuserMediator focuserMediator;
        private FocuserInfo focuserInfo;
        private ITelescopeMediator telescopeMediator;
        private TelescopeInfo telescopeInfo;
        private IRotatorMediator rotatorMediator;
        private RotatorInfo rotatorInfo;
        private WeatherDataInfo weatherDataInfo;
        private IWeatherDataMediator weatherDataMediator;
        private IOptionsVM options;
        private Luckyimaging luckyimaging;
        private BitmapSource _BlackImage;

        [ImportingConstructor]
        public TakeLiveExposures(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IImageSaveMediator imageSaveMediator, IImageHistoryVM imageHistoryVM, IFilterWheelMediator filterWheelMediator, IOptionsVM options, ITelescopeMediator telescopeMediator, IFocuserMediator focuserMediator, IRotatorMediator rotatorMediator, IWeatherDataMediator weatherDataMediator) {
            Gain = -1;
            Offset = -1;
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.profileService = profileService;

            this.cameraMediator = cameraMediator;
            this.cameraMediator.RegisterConsumer(this);

            this.telescopeMediator = telescopeMediator;
            this.telescopeMediator.RegisterConsumer(this);

            this.filterWheelMediator = filterWheelMediator;
            this.filterWheelMediator.RegisterConsumer(this);

            this.focuserMediator = focuserMediator;
            this.focuserMediator.RegisterConsumer(this);

            this.rotatorMediator = rotatorMediator;
            this.rotatorMediator.RegisterConsumer(this);

            this.weatherDataMediator = weatherDataMediator;
            this.weatherDataMediator.RegisterConsumer(this);

            this.options = options;
            luckyimaging = new Luckyimaging(profileService, this.options, imageSaveMediator);
            _BlackImage = BlackImage();
        }

        private TakeLiveExposures(TakeLiveExposures cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.imagingMediator, cloneMe.imageSaveMediator, cloneMe.imageHistoryVM, cloneMe.filterWheelMediator, cloneMe.options, cloneMe.telescopeMediator, cloneMe.focuserMediator, cloneMe.rotatorMediator, cloneMe.weatherDataMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new TakeLiveExposures(this) {
                ExposureTime = ExposureTime,
                ExposureCount = 0,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                TotalExposureCount = TotalExposureCount,
                ProcessImages = ProcessImages,
                SaveToMemory = SaveToMemory,
                FilterOnHfr = FilterOnHfr,
                FilterHfr = FilterHfr,
                FilterOnStars = FilterOnStars,
                FilterStars = FilterStars,
            };

            if (clone.Binning == null) {
                clone.Binning = new BinningMode(1, 1);
            }

            return clone;
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        private double exposureTime;

        [JsonProperty]
        public double ExposureTime {
            get => exposureTime;
            set {
                exposureTime = value;
                RaisePropertyChanged();
            }
        }

        private int gain;

        [JsonProperty]
        public int Gain { get => gain; set { gain = value; RaisePropertyChanged(); } }

        private int offset;

        [JsonProperty]
        public int Offset { get => offset; set { offset = value; RaisePropertyChanged(); } }

        private BinningMode binning;

        [JsonProperty]
        public BinningMode Binning { get => binning; set { binning = value; RaisePropertyChanged(); } }

        private string imageType;

        [JsonProperty]
        public string ImageType { get => imageType; set { imageType = value; RaisePropertyChanged(); } }

        private int exposureCount;

        [JsonProperty]
        public int ExposureCount { get => exposureCount; set { exposureCount = value; RaisePropertyChanged(); } }

        private int totalExposureCount;

        [JsonProperty]
        public int TotalExposureCount { get => totalExposureCount; set { totalExposureCount = value; RaisePropertyChanged(); } }

        private bool processImages;

        [JsonProperty]
        public bool ProcessImages { get => processImages; set { processImages = value; RaisePropertyChanged(); } }

        private bool saveToMemory;

        [JsonProperty]
        public bool SaveToMemory { get => saveToMemory; set { saveToMemory = value; RaisePropertyChanged(); } }

        private bool filterOnHfr;

        [JsonProperty]
        public bool FilterOnHfr { get => filterOnHfr; set { filterOnHfr = value; RaisePropertyChanged(); } }

        private double filterHfr;

        [JsonProperty]
        public double FilterHfr { get => filterHfr; set { filterHfr = value; RaisePropertyChanged(); } }

        private bool filterOnStars;

        [JsonProperty]
        public bool FilterOnStars { get => filterOnStars; set { filterOnStars = value; RaisePropertyChanged(); } }

        private double filterStars;

        [JsonProperty]
        public double FilterStars { get => filterStars; set { filterStars = value; RaisePropertyChanged(); } }

        private CameraInfo cameraInfo;

        public CameraInfo CameraInfo {
            get => cameraInfo;
            private set {
                cameraInfo = value;
                RaisePropertyChanged();
            }
        }

        private ObservableCollection<string> _imageTypes;

        public ObservableCollection<string> ImageTypes {
            get {
                if (_imageTypes == null) {
                    _imageTypes = new ObservableCollection<string>();

                    Type type = typeof(CaptureSequence.ImageTypes);
                    foreach (var p in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)) {
                        var v = p.GetValue(null);
                        _imageTypes.Add(v.ToString());
                    }
                }
                return _imageTypes;
            }
            set {
                _imageTypes = value;
                RaisePropertyChanged();
            }
        }

        public class Frame {
            public int FrameNumber { get; set; }
            public string DateObs { get; set; }
            public string DateMid { get; set; }

            public Frame(int frameNumber, DateTime dateObs, DateTime dateMid) {
                this.FrameNumber = frameNumber;
                this.DateObs = dateObs.ToString("yyyy-MM-ddThh:mm:ss.fff");
                this.DateMid = dateMid.ToString("yyyy-MM-ddThh:mm:ss.fff");
            }

            public sealed class FrameMap : ClassMap<Frame> {

                public FrameMap() {
                    Map(m => m.FrameNumber).Name("FrameNumber");
                    Map(m => m.DateObs).Name("DateObs");
                    Map(m => m.DateMid).Name("DateMid");
                }
            }
        }

        private List<Frame> _frames;

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            ExposureCount = 1;
            LuckyTargetContainer luckyContainer = ItemUtility.RetrieveLuckyContainer(Parent);
            luckyContainer.LuckyRun++;
            options.AddImagePattern(new ImagePattern(luckyimaging.luckyRunPattern.Key, luckyimaging.luckyRunPattern.Description, luckyimaging.luckyRunPattern.Category) {
                Value = $"{luckyContainer.LuckyRun}"
            });
            var capture = new CaptureSequence() {
                ExposureTime = ExposureTime,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ProgressExposureCount = ExposureCount,
                TotalExposureCount = TotalExposureCount,
                EnableSubSample = luckyContainer.EnableSubSample,
                SubSambleRectangle = ItemUtility.RetrieveLuckyTargetRoi(Parent),
            };

            var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token);

            _frames = new List<Frame>();
            List<Task> _tasks = new List<Task>();

            bool _firstImage = true;
            var liveViewEnumerable = cameraMediator.LiveView(capture, localCTS.Token);
            Stopwatch seqDuration = Stopwatch.StartNew();
            await liveViewEnumerable.ForEachAsync(exposureData => {
                token.ThrowIfCancellationRequested();
                if (exposureData != null) {
                    var exposureEnd = DateTime.Now; // first thing is to get the endTime
                    if (_firstImage) {
                        _firstImage = false;
                        seqDuration = Stopwatch.StartNew();
                        if (ExposureTime < 1d)
                            // ignore the first image if it's less than a second, because it could take a while for the camera to start up.
                            return;
                    }
                    // check memory
                    double availableMemoryMb = luckyimaging.MinimumAvailableMemory;
                    if (luckyimaging.MinimumAvailableMemory > 0) {
                        try {
                            PerformanceCounter availableMemoryCounter = new PerformanceCounter("Memory", "Available Bytes");
                            long availableMemoryBytes = Convert.ToInt64(availableMemoryCounter.NextValue());
                            availableMemoryMb = availableMemoryBytes / (1024.0 * 1024.0);
                        } catch (Exception) { /* do nothing */ }
                    }
                    if (availableMemoryMb < luckyimaging.MinimumAvailableMemory) {
                        Logger.Debug($"Available memory less than threshhold: {availableMemoryMb} Mb. Skipping image.");
                        ExposureCount++; // Add to the exposurecount otherwise it might loop forever
                    } else {
                        if (luckyimaging.SaveStatsToCsv) {
                            var exposureStart = exposureEnd.AddSeconds(-ExposureTime);
                            var exposureMid = ((DateTimeMetaDataHeader)exposureData.MetaData.GenericHeaders.FirstOrDefault(x => x.Key == "DATE_MID"))?.Value ?? exposureEnd.AddSeconds(-ExposureTime / 2);
                            _frames.Add(new Frame(ExposureCount, exposureStart, exposureMid));
                        }
                        // Create tasks without starting them
                        int id = ExposureCount;
                        int luckyRun = luckyContainer.LuckyRun;
                        if (SaveToMemory) {
                            _tasks.Add(new Task(() => ProcessExposureData(exposureData, exposureEnd, luckyRun, id, progress, token)));
                        } else {
                            _ = ProcessExposureData(exposureData, exposureEnd, luckyRun, id, progress, token);
                        }

                        if (ExposureCount >= TotalExposureCount) {
                            double fps = ExposureCount / (((double)seqDuration.ElapsedMilliseconds) / 1000);
                            Logger.Info("Captured " + ExposureCount + " times " + ExposureTime + "s live images in " + seqDuration.ElapsedMilliseconds + " ms. : " + Math.Round(fps, 2) + " fps");
                            try {
                                // Log dropped frames for zwo cameras
                                Logger.Debug("Dropped frames: " + cameraMediator.Action("GetDroppedFrames", ""));
                            } catch (Exception) { /*do nothing*/ }
                            localCTS.Cancel();
                        } else { ExposureCount++; }
                    }
                }
            });
            // Start all tasks
            foreach (var task in _tasks) {
                task.Start();
            }

            // wait till camera reconnects. Specifically for QHY camera's
            Thread.Sleep(100);
            while (!cameraInfo.Connected) {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(100);
            }

            if (luckyimaging.SaveStatsToCsv) {
                var target = RetrieveTarget(Parent);
                string csvfile = Path.Combine(profileService.ActiveProfile.ImageFileSettings.FilePath, "FrameList-" + luckyContainer.LuckyRun + "-" + DateTime.Now.ToString("yyyy-MM-dd hh-mm-ss") + ".csv");
                using (var writer = new StreamWriter(csvfile))
                using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                    csv.Context.RegisterClassMap<Frame.FrameMap>();
                    csv.WriteRecords(_frames);
                }
            }

            // Wait for all tasks to complete
            Task.WhenAll(_tasks).Wait();
        }

        private async Task ProcessExposureData(IExposureData exposureData, DateTime exposureEnd, int luckyRun, int luckyImageId, IProgress<ApplicationStatus> progress, CancellationToken token) {
            LuckyTargetContainer luckyContainer = ItemUtility.RetrieveLuckyContainer(Parent);
            var target = RetrieveTarget(Parent);

            var imageParams = new PrepareImageParameters(null, false);
            if (IsLightSequence()) {
                imageParams = new PrepareImageParameters(true, ProcessImages);
            }

            var imageData = await exposureData.ToImageData(progress, token);

            Assembly assembly = Assembly.GetExecutingAssembly();
            imageData.MetaData.GenericHeaders.Add(new StringMetaDataHeader("PLCREATE", "LuckyImaging-" + assembly.GetName().Version.ToString(), "The plugin used to create this file."));

            var exposureStart = exposureEnd.AddSeconds(-ExposureTime);
            imageData.MetaData.Image.ExposureStart = exposureStart;
            imageData.MetaData.Image.ExposureNumber = luckyImageId;
            imageData.MetaData.Image.ExposureTime = ExposureTime;
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("JD-BEG", AstroUtil.GetJulianDate(exposureStart), "Julian exposure start date"));
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("JD-OBS", AstroUtil.GetJulianDate(exposureStart.AddSeconds(ExposureTime / 2)), "Julian exposure mid date"));
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("JD-END", AstroUtil.GetJulianDate(exposureEnd), "Julian exposure end date"));

            imageData.MetaData.GenericHeaders.Add(new IntMetaDataHeader("LUCKYRUN", luckyRun, "Current lucky imaging run for the target"));

            // Only show first and last image in Imaging window
            if (!ProcessImages && (luckyImageId == 1 || luckyImageId % luckyimaging.ShowEveryNthImage == 0 || luckyImageId == TotalExposureCount)) {
                _ = imagingMediator.PrepareImage(imageData, imageParams, token);
            }

            if (IsLightSequence() && ProcessImages) {
                var prepareTask = imagingMediator.PrepareImage(imageData, imageParams, token);
                var renderedImage = await prepareTask;
                var statistics = await renderedImage.RawImageData.Statistics;

                if ((!FilterOnHfr && !FilterOnStars) || luckyImageId == 1 || luckyImageId == TotalExposureCount ||
                    (FilterOnHfr && !FilterOnStars && imageData.StarDetectionAnalysis.HFR < FilterHfr) ||
                    (FilterOnStars && !FilterOnHfr && imageData.StarDetectionAnalysis.DetectedStars > FilterStars) ||
                    (FilterOnHfr && FilterOnStars && imageData.StarDetectionAnalysis.HFR < FilterHfr && imageData.StarDetectionAnalysis.DetectedStars > FilterStars)) {
                    AddMetaData(imageData.MetaData, target, ItemUtility.RetrieveLuckyTargetRoi(Parent));
                    var id = imageHistoryVM.GetNextImageId();
                    imageData.MetaData.Image.Id = id;
                    imageHistoryVM.Add(id, statistics, ImageType);
                    await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);
                }
            } else {
                AddMetaData(imageData.MetaData, target, ItemUtility.RetrieveLuckyTargetRoi(Parent));
                List<ImagePattern> customPatterns = new List<ImagePattern>();
                customPatterns.Add(new ImagePattern(luckyimaging.luckyRunPattern.Key, luckyimaging.luckyRunPattern.Description, luckyimaging.luckyRunPattern.Category) {
                    Value = $"{luckyRun}"
                });
                FileSaveInfo fileSaveInfo = new FileSaveInfo(profileService);
                string tempPath = await imageData.PrepareSave(fileSaveInfo);
                var path = imageData.FinalizeSave(tempPath, fileSaveInfo.FilePattern, customPatterns);
                Logger.Debug(path);
                imageSaveMediator.OnImageSaved(
                    new ImageSavedEventArgs() {
                        MetaData = imageData.MetaData,
                        PathToImage = new Uri(path),
                        Image = _BlackImage,
                        FileType = profileService.ActiveProfile.ImageFileSettings.FileType,
                        Statistics = null,
                        StarDetectionAnalysis = null,
                        Duration = ExposureTime,
                        IsBayered = false,
                        Filter = imageData.MetaData.FilterWheel.Filter
                    }
                );
            }

            return;
        }

        private void AddMetaData(
            ImageMetaData metaData,
            InputTarget target,
            ObservableRectangle subSambleRectangle) {
            
            if (target != null) {
                metaData.Target.Name = target.DeepSkyObject.NameAsAscii;
                metaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                metaData.Target.Rotation = target.PositionAngle;
                metaData.GenericHeaders.Add(new StringMetaDataHeader("TARGETID", target.DeepSkyObject?.Id));
            }

            metaData.Image.ImageType = ImageType;

            // Fill all available info from profile
            metaData.FromProfile(profileService.ActiveProfile);
            metaData.FromCameraInfo(CameraInfo);
            metaData.FromTelescopeInfo(telescopeInfo);
            metaData.FromFilterWheelInfo(filterWheelInfo);
            metaData.FromRotatorInfo(rotatorInfo);
            metaData.FromFocuserInfo(focuserInfo);
            metaData.FromWeatherDataInfo(weatherDataInfo);

            if (metaData.Target.Coordinates == null || double.IsNaN(metaData.Target.Coordinates.RA))
                metaData.Target.Coordinates = metaData.Telescope.Coordinates;

            metaData.GenericHeaders.Add(new StringMetaDataHeader("CAMERA", CameraInfo.Name));

            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("XORGSUBF", subSambleRectangle.X, "X-position of the ROI"));
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("YORGSUBF", subSambleRectangle.Y, "Y-position of the ROI"));
        }

        private BitmapSource BlackImage() {
            WriteableBitmap bitmap = new WriteableBitmap(10, 10, 96, 96, PixelFormats.Gray16, null);

            // Create a buffer to hold the pixel data
            int stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
            int bufferSize = stride * bitmap.PixelHeight;
            byte[] pixels = new byte[bufferSize];

            // Fill the buffer with black pixels
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = 0; // Set the pixel value to 0 (black)
            }

            // Write the pixel data to the bitmap
            bitmap.WritePixels(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), pixels, stride, 0);
            return bitmap;
        }

        private bool IsLightSequence() {
            return ImageType == CaptureSequence.ImageTypes.SNAPSHOT || ImageType == CaptureSequence.ImageTypes.LIGHT;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        private InputTarget RetrieveTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as IDeepSkyObjectContainer;
                if (container != null) {
                    return container.Target;
                } else {
                    return RetrieveTarget(parent.Parent);
                }
            } else {
                return null;
            }
        }

        public bool Validate() {
            var i = new List<string>();
            CameraInfo = this.cameraMediator.GetInfo();
            if (!CameraInfo.Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            } else {
                if (CameraInfo.CanSetGain && Gain > -1 && (Gain < CameraInfo.GainMin || Gain > CameraInfo.GainMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Gain"], CameraInfo.GainMin, CameraInfo.GainMax, Gain));
                }
                if (CameraInfo.CanSetOffset && Offset > -1 && (Offset < CameraInfo.OffsetMin || Offset > CameraInfo.OffsetMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_Offset"], CameraInfo.OffsetMin, CameraInfo.OffsetMax, Offset));
                }
            }
            if (ItemUtility.RetrieveLuckyContainer(Parent) == null) {
                i.Add("This instruction only works within a LuckyTargetContainer.");
            }

            var fileSettings = profileService.ActiveProfile.ImageFileSettings;

            if (string.IsNullOrWhiteSpace(fileSettings.FilePath)) {
                i.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_FilePathEmpty"]);
            } else if (!Directory.Exists(fileSettings.FilePath)) {
                i.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeExposure_Validation_FilePathInvalid"]);
            }

            Issues = i;
            return i.Count == 0;
        }

        public void UpdateDeviceInfo(CameraInfo cameraStatus) {
            CameraInfo = cameraStatus;
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            this.telescopeInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(FilterWheelInfo deviceInfo) {
            this.filterWheelInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            this.focuserInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(RotatorInfo deviceInfo) {
            this.rotatorInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(WeatherDataInfo deviceInfo) {
            this.weatherDataInfo = deviceInfo;
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            ;
        }

        public void UpdateUserFocused(FocuserInfo info) {
            ;
        }

        public void Dispose() {
            this.cameraMediator.RemoveConsumer(this);
            this.telescopeMediator.RemoveConsumer(this);
            this.filterWheelMediator.RemoveConsumer(this);
            this.focuserMediator.RemoveConsumer(this);
            this.rotatorMediator.RemoveConsumer(this);
            this.weatherDataMediator.RemoveConsumer(this);
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(this.ExposureTime * this.TotalExposureCount);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TakeLiveExposures)}, ExposureTime {ExposureTime}, Gain {Gain}, Offset {Offset}, ImageType {ImageType}, Binning {Binning?.Name}";
        }
    }
}