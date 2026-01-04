using NINA.Luckyimaging.Properties;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Settings = NINA.Luckyimaging.Properties.Settings;

namespace NINA.Luckyimaging {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes. Please fill these accoringly
    /// 
    /// An instance of this class will be created and set as datacontext on the plugin options tab in N.I.N.A. to be able to configure global plugin settings
    /// The user interface for the settings will be defined by a DataTemplate with the key having the naming convention "Luckyimaging_Options" where Luckyimaging corresponds to the AssemblyTitle - In this template example it is found in the Options.xaml
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Luckyimaging : PluginBase, INotifyPropertyChanged {
        private readonly IPluginOptionsAccessor pluginSettings;
        private readonly IImageSaveMediator imageSaveMediator;

        public ImagePattern luckyRunPattern = new ImagePattern("$$LUCKYRUN$$", "Current lucky imaging run for the target", "LuckyImaging");

        public ImagePattern roiXPattern = new ImagePattern("$$ROIX$$", "X-position of the ROI", "LuckyImaging");
        public ImagePattern roiYPattern = new ImagePattern("$$ROIY$$", "Y-position of the ROI", "LuckyImaging");

        [ImportingConstructor]
        public Luckyimaging(IProfileService profileService, IOptionsVM options, IImageSaveMediator imageSaveMediator) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            // This helper class can be used to store plugin settings that are dependent on the current profile
            this.pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));
            this.imageSaveMediator = imageSaveMediator;

            luckyRunPattern.Value = "0";
            roiXPattern.Value = "0";
            roiYPattern.Value = "0";

            options.AddImagePattern(luckyRunPattern);
            options.AddImagePattern(roiXPattern);
            options.AddImagePattern(roiYPattern);

            this.imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;
        }

        public override Task Teardown() {
            // Make sure to unregister an event when the object is no longer in use. Otherwise garbage collection will be prevented.
            this.imageSaveMediator.BeforeFinalizeImageSaved -= ImageSaveMediator_BeforeFinalizeImageSaved;
            return base.Teardown();
        }

        public int ShowEveryNthImage {
            get {
                return Settings.Default.ShowEveryNthImage;
            }
            set {
                Settings.Default.ShowEveryNthImage = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool SaveStatsToCsv {
            get {
                return Settings.Default.SaveStatsToCsv;
            }
            set {
                Settings.Default.SaveStatsToCsv = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }
        //MinimumAvailableMemory
        public int MinimumAvailableMemory {
            get {
                return Settings.Default.MinimumAvailableMemory;
            }
            set {
                Settings.Default.MinimumAvailableMemory = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double DateObsOffset {
            get {
                return Settings.Default.DateObsOffset;
            }
            set {
                Settings.Default.DateObsOffset = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        private Task ImageSaveMediator_BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
            // Normal images saved will get an empty value
            e.AddImagePattern(new ImagePattern(luckyRunPattern.Key, luckyRunPattern.Description, luckyRunPattern.Category) {
                Value = string.Empty
            });
            return Task.CompletedTask;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
