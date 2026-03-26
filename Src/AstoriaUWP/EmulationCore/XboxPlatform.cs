// XboxPlatform - Provides Xbox One UWP / Windows RT compatibility utilities.
// Handles platform detection, gamepad input mapping, TV safe area, and
// API compatibility for Xbox One, ARM (Surface RT/HoloLens), and x64 devices.

using System;
using System.Diagnostics;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.System.Profile;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace DalvikUWPCSharp.Classes
{
    /// <summary>
    /// Identifies the UWP device platform family for feature toggling.
    /// </summary>
    public enum DevicePlatform
    {
        Desktop,
        Xbox,
        Mobile,
        IoT,
        HoloLens,
        Unknown
    }

    /// <summary>
    /// Identifies the processor architecture for native library selection.
    /// </summary>
    public enum ProcessorArch
    {
        X86,
        X64,
        Arm,
        Arm64,
        Unknown
    }

    /// <summary>
    /// Provides Xbox One UWP compatibility, platform detection, gamepad input mapping,
    /// and TV safe area management. Ensures the Android emulator works correctly on
    /// Xbox One (in UWP Dev Mode), Windows RT ARM devices, and standard x64 PCs.
    /// </summary>
    public static class XboxPlatform
    {
        private static DevicePlatform? cachedPlatform;
        private static ProcessorArch? cachedArch;

        /// <summary>
        /// Detects the current device platform (Desktop, Xbox, Mobile, IoT, HoloLens).
        /// </summary>
        public static DevicePlatform GetCurrentPlatform()
        {
            if (cachedPlatform.HasValue)
                return cachedPlatform.Value;

            string family = AnalyticsInfo.VersionInfo.DeviceFamily;
            switch (family)
            {
                case "Windows.Desktop":
                    cachedPlatform = DevicePlatform.Desktop;
                    break;
                case "Windows.Xbox":
                    cachedPlatform = DevicePlatform.Xbox;
                    break;
                case "Windows.Mobile":
                    cachedPlatform = DevicePlatform.Mobile;
                    break;
                case "Windows.IoT":
                    cachedPlatform = DevicePlatform.IoT;
                    break;
                case "Windows.Holographic":
                    cachedPlatform = DevicePlatform.HoloLens;
                    break;
                default:
                    cachedPlatform = DevicePlatform.Unknown;
                    break;
            }

            Debug.WriteLine("[XboxPlatform] Detected platform: " + cachedPlatform.Value +
                " (DeviceFamily=" + family + ")");
            return cachedPlatform.Value;
        }

        /// <summary>
        /// Detects the processor architecture for native library loading.
        /// </summary>
        public static ProcessorArch GetProcessorArchitecture()
        {
            if (cachedArch.HasValue)
                return cachedArch.Value;

            var arch = Windows.ApplicationModel.Package.Current.Id.Architecture;
            switch (arch)
            {
                case Windows.System.ProcessorArchitecture.X86:
                    cachedArch = ProcessorArch.X86;
                    break;
                case Windows.System.ProcessorArchitecture.X64:
                    cachedArch = ProcessorArch.X64;
                    break;
                case Windows.System.ProcessorArchitecture.Arm:
                    cachedArch = ProcessorArch.Arm;
                    break;
                default:
                    cachedArch = ProcessorArch.Unknown;
                    break;
            }

            Debug.WriteLine("[XboxPlatform] Processor architecture: " + cachedArch.Value);
            return cachedArch.Value;
        }

        /// <summary>
        /// Returns true if running on Xbox One or Xbox Series X|S.
        /// </summary>
        public static bool IsXbox()
        {
            return GetCurrentPlatform() == DevicePlatform.Xbox;
        }

        /// <summary>
        /// Returns true if running on an ARM device (Surface RT, HoloLens, etc.).
        /// </summary>
        public static bool IsArm()
        {
            var arch = GetProcessorArchitecture();
            return arch == ProcessorArch.Arm || arch == ProcessorArch.Arm64;
        }

        /// <summary>
        /// Returns the appropriate Android native library subfolder name for the current architecture.
        /// Maps UWP architectures to Android ABI names (armeabi-v7a, arm64-v8a, x86, x86_64).
        /// </summary>
        public static string GetAndroidAbiName()
        {
            switch (GetProcessorArchitecture())
            {
                case ProcessorArch.Arm: return "armeabi-v7a";
                case ProcessorArch.Arm64: return "arm64-v8a";
                case ProcessorArch.X86: return "x86";
                case ProcessorArch.X64: return "x86_64";
                default: return "armeabi-v7a"; // Default fallback
            }
        }

        /// <summary>
        /// Applies Xbox One TV safe area margins to a page. Xbox displays often overscan,
        /// so a safe area margin ensures content isn't clipped at screen edges.
        /// On non-Xbox platforms, this is a no-op.
        /// </summary>
        public static void ApplyTvSafeArea(Page page)
        {
            if (!IsXbox())
                return;

            // Xbox TV safe area: typically 48px on each side for 1080p
            ApplicationView.GetForCurrentView().SetDesiredBoundsMode(
                ApplicationViewBoundsMode.UseCoreWindow);

            if (page.Content is FrameworkElement content)
            {
                content.Margin = new Thickness(48, 27, 48, 27);
            }

            Debug.WriteLine("[XboxPlatform] Applied TV safe area margins");
        }

        /// <summary>
        /// Configures focus navigation for Xbox gamepad/remote input.
        /// Enables D-pad/analog stick navigation between XAML controls.
        /// On non-Xbox platforms, this is a no-op.
        /// </summary>
        public static void EnableGamepadNavigation()
        {
            if (!IsXbox())
                return;

            // Enable gamepad focus navigation
            Application.Current.RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;

            Debug.WriteLine("[XboxPlatform] Enabled gamepad focus navigation");
        }

        /// <summary>
        /// Returns whether the StatusBar API is available (Windows Mobile only).
        /// </summary>
        public static bool HasStatusBar()
        {
            return ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar");
        }

        /// <summary>
        /// Returns the display scale factor for the current device.
        /// Useful for converting Android dp units to UWP effective pixels.
        /// </summary>
        public static double GetDisplayScaleFactor()
        {
            try
            {
                var displayInfo = Windows.Graphics.Display.DisplayInformation.GetForCurrentView();
                return displayInfo.RawPixelsPerViewPixel;
            }
            catch
            {
                return 1.0;
            }
        }

        /// <summary>
        /// Gets the screen resolution category for Android resource qualifier matching.
        /// Maps UWP display DPI to Android density buckets (ldpi, mdpi, hdpi, xhdpi, xxhdpi).
        /// </summary>
        public static string GetAndroidDensityQualifier()
        {
            try
            {
                var displayInfo = Windows.Graphics.Display.DisplayInformation.GetForCurrentView();
                double dpi = displayInfo.LogicalDpi;

                if (dpi <= 120) return "ldpi";
                if (dpi <= 160) return "mdpi";
                if (dpi <= 240) return "hdpi";
                if (dpi <= 320) return "xhdpi";
                if (dpi <= 480) return "xxhdpi";
                return "xxxhdpi";
            }
            catch
            {
                return "mdpi";
            }
        }

        /// <summary>
        /// Maps Android touch input to Xbox gamepad input.
        /// Provides basic mapping: A button = tap, B button = back,
        /// left stick = scroll, D-pad = focus navigation.
        /// </summary>
        public static AndroidInputAction MapGamepadToAndroid(VirtualKey key)
        {
            switch (key)
            {
                case VirtualKey.GamepadA:
                    return AndroidInputAction.Tap;
                case VirtualKey.GamepadB:
                    return AndroidInputAction.Back;
                case VirtualKey.GamepadX:
                    return AndroidInputAction.Menu;
                case VirtualKey.GamepadY:
                    return AndroidInputAction.Search;
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                    return AndroidInputAction.DpadUp;
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                    return AndroidInputAction.DpadDown;
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadLeftThumbstickLeft:
                    return AndroidInputAction.DpadLeft;
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.GamepadLeftThumbstickRight:
                    return AndroidInputAction.DpadRight;
                case VirtualKey.GamepadMenu:
                    return AndroidInputAction.Menu;
                case VirtualKey.GamepadView:
                    return AndroidInputAction.Home;
                case VirtualKey.GamepadLeftShoulder:
                    return AndroidInputAction.VolumeDown;
                case VirtualKey.GamepadRightShoulder:
                    return AndroidInputAction.VolumeUp;
                default:
                    return AndroidInputAction.None;
            }
        }

        /// <summary>
        /// Logs platform diagnostic information for debugging.
        /// </summary>
        public static void LogPlatformInfo()
        {
            Debug.WriteLine("=== AstoriaUWP Platform Info ===");
            Debug.WriteLine("Platform: " + GetCurrentPlatform());
            Debug.WriteLine("Architecture: " + GetProcessorArchitecture());
            Debug.WriteLine("Android ABI: " + GetAndroidAbiName());
            Debug.WriteLine("Display scale: " + GetDisplayScaleFactor());
            Debug.WriteLine("Android density: " + GetAndroidDensityQualifier());
            Debug.WriteLine("Status bar available: " + HasStatusBar());
            Debug.WriteLine("Is Xbox: " + IsXbox());
            Debug.WriteLine("Is ARM: " + IsArm());
            Debug.WriteLine("================================");
        }
    }

    /// <summary>
    /// Android input actions mapped from Xbox gamepad buttons.
    /// </summary>
    public enum AndroidInputAction
    {
        None,
        Tap,
        Back,
        Home,
        Menu,
        Search,
        DpadUp,
        DpadDown,
        DpadLeft,
        DpadRight,
        VolumeUp,
        VolumeDown
    }
}
