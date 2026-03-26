using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using System;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace AndroidInteropLib.android.widget
{
    public class ImageView : View
    {
        protected Windows.UI.Xaml.Controls.Image image = new Windows.UI.Xaml.Controls.Image();

        public const int SCALE_TYPE_CENTER = 5;
        public const int SCALE_TYPE_CENTER_CROP = 6;
        public const int SCALE_TYPE_CENTER_INSIDE = 7;
        public const int SCALE_TYPE_FIT_CENTER = 3;
        public const int SCALE_TYPE_FIT_END = 4;
        public const int SCALE_TYPE_FIT_START = 2;
        public const int SCALE_TYPE_FIT_XY = 1;
        public const int SCALE_TYPE_MATRIX = 0;

        public ImageView(Context context) : base(context) { }
        public ImageView(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            // Image inherits FrameworkElement (not ContentControl), so use WinUI.Content.
            image.Stretch = Stretch.Uniform;
            WinUI.Content = image;
        }

        public void setImageBitmap(object bitmap) { }

        public void setImageResource(int resId) { }

        public void setImageURI(Uri uri)
        {
            if (uri != null)
                image.Source = new BitmapImage(uri);
        }

        public void setScaleType(int scaleType)
        {
            switch (scaleType)
            {
                case SCALE_TYPE_FIT_XY:
                    image.Stretch = Stretch.Fill;
                    break;
                case SCALE_TYPE_CENTER_CROP:
                    image.Stretch = Stretch.UniformToFill;
                    break;
                case SCALE_TYPE_CENTER_INSIDE:
                    image.Stretch = Stretch.None;
                    break;
                default:
                    image.Stretch = Stretch.Uniform;
                    break;
            }
        }

        public void setAdjustViewBounds(bool adjustViewBounds) { }
        public void setColorFilter(int color) { }

        public new void setAlpha(int alpha)
        {
            WinUI.Opacity = alpha / 255.0;
        }
    }
}
