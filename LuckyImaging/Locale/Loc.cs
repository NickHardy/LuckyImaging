using NINA.Core.Locale;
using NINA.Core.Utility;
using System;
using System.Globalization;
using System.Resources;
using System.Windows.Data;

namespace NINA.Luckyimaging.Locale {
    public class Loc : BaseINPC, ILoc {
        private ResourceManager _locale;
        private CultureInfo _activeCulture;

        private static readonly Lazy<Loc> lazy =
         new Lazy<Loc>(() => new Loc());

        private Loc() {
            _locale = new ResourceManager("NINA.Luckyimaging.Locale.Locale", typeof(Loc).Assembly);
        }

        public void ReloadLocale(string culture) {
            using (MyStopWatch.Measure()) {
                try {
                    _activeCulture = new CultureInfo(culture);
                } catch (Exception ex) {
                    Logger.Error(ex);
                }
                RaiseAllPropertiesChanged();
            }
        }

        public static Loc Instance => lazy.Value;

        public string this[string key] {
            get {
                if (key == null) {
                    return string.Empty;
                }
                return this._locale?.GetString(key, this._activeCulture) ?? $"MISSING LABEL {key}";
            }
        }
    }

    public class LocExtension : Binding {

        public LocExtension(string name) : base($"[{name}]") {
            this.Mode = BindingMode.OneWay;
            this.Source = Loc.Instance;
        }
    }
}
