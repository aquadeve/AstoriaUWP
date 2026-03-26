using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using AndroidInteropLib.android.view;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace AndroidInteropLib.android.support.v7.widget
{
    /// <summary>
    /// A FrameLayout with a rounded corner background and shadow.
    /// Maps to UWP Border with rounded corners and white background.
    /// </summary>
    public class CardView : ViewGroup
    {
        private Border card = new Border();
        private Grid content = new Grid();

        public CardView(Context c, AttributeSet a) : base(c, a) { }

        public override void CreateWinUI(params object[] obj)
        {
            card.CornerRadius = new Windows.UI.Xaml.CornerRadius(4);
            card.Background = new SolidColorBrush(Windows.UI.Colors.White);
            card.Margin = new Thickness(8);
            card.Padding = new Thickness(8);
            card.Child = content;
            WinUI.Content = card;
        }

        public void setCardElevation(float elevation) { }

        public float getCardElevation() { return 0f; }

        public void setCardBackgroundColor(int color)
        {
            card.Background = new SolidColorBrush(ticomware.interop.Util.IntToColor(color));
        }

        public void setRadius(float radius)
        {
            card.CornerRadius = new Windows.UI.Xaml.CornerRadius(radius);
        }

        public float getRadius() { return (float)card.CornerRadius.TopLeft; }

        public void setContentPadding(int left, int top, int right, int bottom)
        {
            card.Padding = new Thickness(left, top, right, bottom);
        }

        public void setPreventCornerOverlap(bool preventCornerOverlap) { }

        public void setUseCompatPadding(bool useCompatPadding) { }

        public override void addView(View view)
        {
            addView(view, null);
        }

        public override void addView(View view, LayoutParams param)
        {
            content.Children.Add(view.WinUI);
        }

        public override void removeView(View view)
        {
            content.Children.Remove(view.WinUI);
        }
    }
}
