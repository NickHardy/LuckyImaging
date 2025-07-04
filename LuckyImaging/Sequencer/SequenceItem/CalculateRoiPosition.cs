﻿#region "copyright"

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
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Validations;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Utility.WindowService;
using NINA.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Locale;
using NINA.Equipment.Model;
using NINA.Core.Model.Equipment;
using NINA.WPF.Base.ViewModel;
using NINA.PlateSolving.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Core.Enum;
using NINA.Image.Interfaces;
using NINA.Core.Utility.Notification;
using static NINA.Astrometry.Coordinates;
using NINA.Luckyimaging.Sequencer.Utility;
using System.Windows;
using NINA.Image.FileFormat;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Image.ImageData;

namespace NINA.Luckyimaging.Sequencer.SequenceItem {

    [ExportMetadata("Name", "Calculate Roi Position")]
    [ExportMetadata("Description", "Platesolve an image locate the target and center the ROI position on the target.")]
    [ExportMetadata("Icon", "CrosshairSVG")]
    [ExportMetadata("Category", "LuckyImaging")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class CalculateRoiPosition : NINA.Sequencer.SequenceItem.SequenceItem, IValidatable {
        private IProfileService profileService;
        private ITelescopeMediator telescopeMediator;
        private IImagingMediator imagingMediator;
        private IFilterWheelMediator filterWheelMediator;
        private IPlateSolverFactory plateSolverFactory;
        private IWindowServiceFactory windowServiceFactory;
        private IImageHistoryVM imageHistoryVM;
        private IImageSaveMediator imageSaveMediator;
        public PlateSolvingStatusVM PlateSolveStatusVM { get; } = new PlateSolvingStatusVM();

        [ImportingConstructor]
        public CalculateRoiPosition(IProfileService profileService,
                            ITelescopeMediator telescopeMediator,
                            IImagingMediator imagingMediator,
                            IFilterWheelMediator filterWheelMediator,
                            IPlateSolverFactory plateSolverFactory,
                            IWindowServiceFactory windowServiceFactory,
                            IImageHistoryVM imageHistoryVM,
                            IImageSaveMediator imageSaveMediator) {
            this.profileService = profileService;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            this.imageHistoryVM = imageHistoryVM;
            this.imageSaveMediator = imageSaveMediator;
        }

        private CalculateRoiPosition(CalculateRoiPosition cloneMe) : this(cloneMe.profileService,
                                                          cloneMe.telescopeMediator,
                                                          cloneMe.imagingMediator,
                                                          cloneMe.filterWheelMediator,
                                                          cloneMe.plateSolverFactory,
                                                          cloneMe.windowServiceFactory,
                                                          cloneMe.imageHistoryVM,
                                                          cloneMe.imageSaveMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            var clone = new CalculateRoiPosition(this);
            clone.ImageFlippedX = ImageFlippedX;
            clone.ImageFlippedY = ImageFlippedY;
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

        private bool _ImageFlippedX;
        [JsonProperty]
        public bool ImageFlippedX { get => _ImageFlippedX; set { _ImageFlippedX = value; RaisePropertyChanged(); } }

        private bool _ImageFlippedY;
        [JsonProperty]
        public bool ImageFlippedY { get => _ImageFlippedY; set { _ImageFlippedY = value; RaisePropertyChanged(); } }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            // save filter to restore after positioning
            var filter = filterWheelMediator.GetInfo()?.SelectedFilter;
            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
            var luckyContainer = ItemUtility.RetrieveLuckyContainer(Parent);

            var parameter = new CaptureSolverParameter() {
                Attempts = profileService.ActiveProfile.PlateSolveSettings.NumberOfAttempts,
                Binning = profileService.ActiveProfile.PlateSolveSettings.Binning,
                Coordinates = telescopeMediator.GetCurrentPosition(),
                DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                ReattemptDelay = TimeSpan.FromMinutes(profileService.ActiveProfile.PlateSolveSettings.ReattemptDelay),
                Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                BlindFailoverEnabled = profileService.ActiveProfile.PlateSolveSettings.BlindFailoverEnabled
            };
            Logger.Debug("PlateSolve parameters: " + JsonConvert.SerializeObject(parameter));

            var seq = new CaptureSequence(
                profileService.ActiveProfile.PlateSolveSettings.ExposureTime,
                CaptureSequence.ImageTypes.LIGHT,
                profileService.ActiveProfile.PlateSolveSettings.Filter,
                new BinningMode(profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.PlateSolveSettings.Binning),
                1
            );

            Logger.Debug("Capturing image for platesolve.");
            var exposureData = await imagingMediator.CaptureImage(seq, token, progress);
            var imageData = await exposureData.ToImageData(progress, token);

            var prepareTask = imagingMediator.PrepareImage(imageData, new PrepareImageParameters(true, true), token);
            var image = prepareTask.Result;

            var imageSolver = new ImageSolver(plateSolver, null);

            Logger.Debug("Solving image");
            var plateSolveResult = await imageSolver.Solve(image.RawImageData, parameter, progress, token);
            if (plateSolveResult.Success) {
                Logger.Debug("PlateSolveResult: " + JsonConvert.SerializeObject(plateSolveResult));
                Logger.Debug("Calculating target position");
                var arcsecPerPix = AstroUtil.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize * profileService.ActiveProfile.PlateSolveSettings.Binning, profileService.ActiveProfile.TelescopeSettings.FocalLength);
                var width = image.Image.PixelWidth;
                var height = image.Image.PixelHeight;
                var center = new Point(width / 2, height / 2);

                //Translate your coordinates to x/y in relation to center coordinates
                var inputTarget = ItemUtility.RetrieveInputTarget(Parent);
                Point targetPoint = inputTarget.InputCoordinates.Coordinates.XYProjection(plateSolveResult.Coordinates, center, arcsecPerPix, arcsecPerPix, plateSolveResult.PositionAngle, ProjectionType.Gnomonic);
                Logger.Debug("Found target at " + targetPoint.X + "x" + targetPoint.Y);

                // Check if the target is in the image
                if (targetPoint.X < 0 || targetPoint.X > width || targetPoint.Y < 0 || targetPoint.Y > height) {
                    Notification.ShowError("TargetPoint is not within the image.");
                    throw new SequenceEntityFailedException("Calculation failed. Target outside of image");
                }

                // Check if we need to flip the targetPoint
                if (ImageFlippedX) { targetPoint.X = width - targetPoint.X; }
                if (ImageFlippedY) { targetPoint.Y = height - targetPoint.Y; }

                // Place the Roi around the star but within the image.
                luckyContainer.X = Math.Min(Math.Max(Math.Round(targetPoint.X * profileService.ActiveProfile.PlateSolveSettings.Binning - (luckyContainer.Width / 2), 0), 0), image.Image.PixelWidth * profileService.ActiveProfile.PlateSolveSettings.Binning - (luckyContainer.Width / 2));
                luckyContainer.Y = Math.Min(Math.Max(Math.Round(targetPoint.Y * profileService.ActiveProfile.PlateSolveSettings.Binning - (luckyContainer.Height / 2), 0), 0), image.Image.PixelHeight * profileService.ActiveProfile.PlateSolveSettings.Binning - (luckyContainer.Height / 2));
                Logger.Debug("Setting roi position to " + luckyContainer.X + "x" + luckyContainer.Y);
            }

            var target = luckyContainer.Target;
            if (target != null) {
                imageData.MetaData.Target.Name = !plateSolveResult.Success ? target.TargetName + "_failed" : target.TargetName;
                imageData.MetaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                imageData.MetaData.Target.PositionAngle = plateSolveResult.PositionAngle;
            }

            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIX", luckyContainer.X, "X-position of the ROI"));
            imageData.MetaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIY", luckyContainer.Y, "Y-position of the ROI"));

            await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);
            imageHistoryVM.Add(imageData.MetaData.Image.Id, await imageData.Statistics, CaptureSequence.ImageTypes.LIGHT);

            // Switch filter back to the saved position
            if (filter != null) {
                _ = await filterWheelMediator.ChangeFilter(filter, token, progress);
            }
            if (!plateSolveResult.Success) {
                throw new SequenceEntityFailedException("Calculation failed to platesolve.");
            }
        }

        public virtual bool Validate() {
            var i = new List<string>();
            if (!telescopeMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }
            if (ItemUtility.RetrieveLuckyContainer(Parent) == null) {
                i.Add("This instruction only works within a LuckyTargetContainer.");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(CalculateRoiPosition)}";
        }
    }
}