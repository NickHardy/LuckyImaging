#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Sequencer.Container;
using NINA.Core.Utility;
using NINA.Luckyimaging.Sequencer.Container;
using System.Windows.Media.Imaging;
using System.IO;

namespace NINA.Luckyimaging.Sequencer.Utility {

    public class ItemUtility {

        public static LuckyTargetContainer RetrieveLuckyContainer(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as LuckyTargetContainer;
                if (container != null) {
                    return container;
                }
                else {
                    return RetrieveLuckyContainer(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static ObservableRectangle RetrieveLuckyTargetRoi(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as LuckyTargetContainer;
                if (container != null) {
                    return container.SubSampleRectangle;
                }
                else {
                    return RetrieveLuckyTargetRoi(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static InputTarget RetrieveInputTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as LuckyTargetContainer;
                if (container != null) {
                    return container.Target;
                }
                else {
                    return RetrieveInputTarget(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static IDeepSkyObjectContainer RetrieveDeepSkyContainer(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as IDeepSkyObjectContainer;
                if (container != null) {
                    return container;
                } else {
                    return RetrieveDeepSkyContainer(parent.Parent);
                }
            } else {
                return null;
            }
        }

    }
}