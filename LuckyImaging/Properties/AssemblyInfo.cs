using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("12a7ca33-1aa5-41e4-8207-b62f4a9968bd")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("2.0.1.1")]
[assembly: AssemblyFileVersion("2.0.1.1")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("LuckyImaging")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Plugin for lucky imaging")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Nick Hardy")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("LuckyImaging")]
[assembly: AssemblyCopyright("Copyright © 2022 Nick Hardy")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1001")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://bitbucket.org/NickHardy/luckyimaging/src/main/")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://nighttime-imaging.eu/")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Lucky Imaging")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://bitbucket.org/NickHardy/luckyimaging/commits/")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://bitbucket.org/NickHardy/luckyimaging/downloads/LuckyImagingExample.png")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"Plugin for getting images fast through video mode.

You will need to add the 'Lucky Target Container' and within that add the 'Take Video Roi Exposures' for taking images.  
Please add these imagepath variables to your filename: $$LUCKYRUN$$ $$FRAMENR$$  
This way your filenames will be unique.  
The LUCKYRUN variable will increase everytime you run the 'Take Video Roi Exposures' instruction and you can view/set it in the container.  
Also note that one run of the 'Take Video Roi Exposures' instruction will be seen as taking one image.
So if you want 500 images of 1 second, it will be seen as a 500 second image. There will also be some overhead, so it's not completely accurate.
But if you want to dither or autofocus or things like that, don't create a very large instruction.  

*Take Video Roi Exposures*
I have added a 'process images' button. If your imaging pc is up to it, you can turn this on, it will run star detection and statistics and they will show up in the imaging history.
You can also choose to discard images based on the Hfr being to high or starcount being to low.
To increase the speed as much as possible, turn the process images off and the lucky images won't show up in the history and will not trigger any autofocus or center after drift triggers, because they are not processed like normal images.
It will then only show every nth image in the image viewer, you can set it in the options. Depending on your imaging-pc you can set this higher or lower.
Please take a 'normal' image every once in a while to be able to use those triggers.

The 'Calculate Roi Position' can be used to center the ROI on your target. It will platesolve the full image and after a successful platesolve it will center the ROI on to the target by updating the x,y coordinates in the 'Lucky Target Container'.
The full image will also be saved and processed as a 'normal' image, so if the platesolve fails, you'll have an image to find out why.

Hope it's clear, let me know if it isn't.

Cheers Nick


If you have any ideas or want to report an issue, please contact me in the [Nina discord server](https://discord.gg/rWRbVbw) and tag me: @NickHolland#5257 

If you would like to buy me a whisky: [click here](https://www.paypal.com/paypalme/NickHardyHolland)

")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]