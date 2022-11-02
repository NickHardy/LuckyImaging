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
using NINA.Luckyimaging.Sequencer.Utility;
using NINA.Luckyimaging.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Image.ImageData;

namespace NINA.Luckyimaging.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Take Roi Exposure")]
    [ExportMetadata("Description", "Lbl_SequenceItem_Imaging_TakeSubframeExposure_Description")]
    [ExportMetadata("Icon", "CameraSVG")]
    [ExportMetadata("Category", "LuckyImaging")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TakeRoiExposure : NINA.Sequencer.SequenceItem.SequenceItem, IExposureItem, IValidatable {
        private ICameraMediator cameraMediator;
        private IImagingMediator imagingMediator;
        private IImageSaveMediator imageSaveMediator;
        private IImageHistoryVM imageHistoryVM;
        private IProfileService profileService;
        private IOptionsVM options;
        private Luckyimaging luckyimaging;

        [ImportingConstructor]
        public TakeRoiExposure(IProfileService profileService, ICameraMediator cameraMediator, IImagingMediator imagingMediator, IImageSaveMediator imageSaveMediator, IImageHistoryVM imageHistoryVM, IOptionsVM options) {
            Gain = -1;
            Offset = -1;
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.profileService = profileService;
            this.options = options;
            CameraInfo = this.cameraMediator.GetInfo();
            luckyimaging = new Luckyimaging(profileService, this.options, imageSaveMediator);
        }

        private TakeRoiExposure(TakeRoiExposure cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.imagingMediator, cloneMe.imageSaveMediator, cloneMe.imageHistoryVM, cloneMe.options) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new TakeRoiExposure(this) {
                ExposureTime = ExposureTime,
                ExposureCount = 0,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType
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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var count = ExposureCount;
            LuckyTargetContainer luckyContainer = ItemUtility.RetrieveLuckyContainer(Parent);
            var target = ItemUtility.RetrieveInputTarget(Parent);

            var info = cameraMediator.GetInfo();
            if(!info.CanSubSample) {
                Logger.Warning($"The camera is not able to take sub frames");
            }

            var capture = new CaptureSequence() {
                ExposureTime = ExposureTime,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ProgressExposureCount = count,
                TotalExposureCount = count + 1,
                EnableSubSample = true,
                SubSambleRectangle = ItemUtility.RetrieveLuckyTargetRoi(Parent),
            };

            var imageParams = new PrepareImageParameters(null, false);
            if (IsLightSequence()) {
                imageParams = new PrepareImageParameters(true, true);
            }

            var exposureData = await imagingMediator.CaptureImage(capture, token, progress);

            var imageData = await exposureData.ToImageData(progress, token);

            var prepareTask = imagingMediator.PrepareImage(imageData, imageParams, token);

            if (target != null) {
                imageData.MetaData.Target.Name = target.DeepSkyObject.NameAsAscii;
                imageData.MetaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                imageData.MetaData.Target.Rotation = target.Rotation;
            }

            imageData.MetaData.GenericHeaders.Add(new IntMetaDataHeader("LUCKYRUN", luckyContainer.LuckyRun, "Current lucky imaging run for the target"));
            options.AddImagePattern(new ImagePattern(luckyimaging.luckyRunPattern.Key, luckyimaging.luckyRunPattern.Description, luckyimaging.luckyRunPattern.Category) {
                Value = $"{luckyContainer.LuckyRun}"
            });
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIX", capture.SubSambleRectangle.X, "X-position of the ROI"));
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIY", capture.SubSambleRectangle.Y, "Y-position of the ROI"));
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("XORGSUBF", capture.SubSambleRectangle.X, "X-position of the ROI"));
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("YORGSUBF", capture.SubSambleRectangle.Y, "Y-position of the ROI"));

            await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);

            if (IsLightSequence()) {
                imageHistoryVM.Add(imageData.MetaData.Image.Id, await imageData.Statistics, ImageType);
            }

            ExposureCount++;
        }

        private bool IsLightSequence() {
            return ImageType == CaptureSequence.ImageTypes.SNAPSHOT || ImageType == CaptureSequence.ImageTypes.LIGHT;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public bool Validate() {
            var i = new List<string>();
            CameraInfo = this.cameraMediator.GetInfo();
            if (!CameraInfo.Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            } else {
                if (CameraInfo.CanSetGain && Gain > -1 && (Gain < CameraInfo.GainMin || Gain > CameraInfo.GainMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeSubframeExposure_Validation_Gain"], CameraInfo.GainMin, CameraInfo.GainMax, Gain));
                }
                if (CameraInfo.CanSetOffset && Offset > -1 && (Offset < CameraInfo.OffsetMin || Offset > CameraInfo.OffsetMax)) {
                    i.Add(string.Format(Loc.Instance["Lbl_SequenceItem_Imaging_TakeSubframeExposure_Validation_Offset"], CameraInfo.OffsetMin, CameraInfo.OffsetMax, Offset));
                }
            }
            if (ItemUtility.RetrieveLuckyContainer(Parent) == null) {
                i.Add("This instruction only works within a LuckyTargetContainer.");
            }

            var fileSettings = profileService.ActiveProfile.ImageFileSettings;

            if (string.IsNullOrWhiteSpace(fileSettings.FilePath)) {
                i.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeSubframeExposure_Validation_FilePathEmpty"]);
            } else if (!Directory.Exists(fileSettings.FilePath)) {
                i.Add(Loc.Instance["Lbl_SequenceItem_Imaging_TakeSubframeExposure_Validation_FilePathInvalid"]);
            }

            Issues = i;
            return i.Count == 0;
        }

        public override TimeSpan GetEstimatedDuration() {
            return TimeSpan.FromSeconds(this.ExposureTime);
        }

        public override string ToString() {
            var currentGain = Gain == -1 ? CameraInfo.DefaultGain : Gain;
            var currentOffset = Offset == -1 ? CameraInfo.DefaultOffset : Offset;
            return $"Category: {Category}, Item: {nameof(TakeRoiExposure)}, ExposureTime {ExposureTime}, Gain {currentGain}, Offset {currentOffset}, ImageType {ImageType}, Binning {Binning?.Name ?? "1x1"}";
        }
    }
}