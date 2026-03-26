using AndroidInteropLib.android.content;
using AndroidInteropLib.android.util;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace AndroidInteropLib.android.widget
{
    /// <summary>
    /// A widget that shows a rating in stars. Maps to star-icon TextBlocks in a StackPanel.
    /// </summary>
    public class RatingBar : View
    {
        private StackPanel panel = new StackPanel { Orientation = Orientation.Horizontal };
        private int numStars = 5;
        private float rating = 0f;

        public interface OnRatingBarChangeListener
        {
            void onRatingChanged(RatingBar ratingBar, float rating, bool fromUser);
        }

        private OnRatingBarChangeListener listener;

        public RatingBar(Context context, AttributeSet attrs) : base(context, attrs) { }

        public override void CreateWinUI(params object[] obj)
        {
            UpdateStars();
            WinUI.Content = panel;
        }

        private void UpdateStars()
        {
            panel.Children.Clear();
            for (int i = 1; i <= numStars; i++)
            {
                TextBlock star = new TextBlock
                {
                    Text = i <= rating ? "★" : "☆",
                    FontSize = 24,
                    Margin = new Thickness(2)
                };
                panel.Children.Add(star);
            }
        }

        public void setNumStars(int num)
        {
            numStars = num;
            UpdateStars();
        }

        public int getNumStars() { return numStars; }

        public void setRating(float r)
        {
            rating = r;
            UpdateStars();
        }

        public float getRating() { return rating; }

        public void setStepSize(float stepSize) { }

        public void setIsIndicator(bool indicator) { }

        public void setOnRatingBarChangeListener(OnRatingBarChangeListener l)
        {
            listener = l;
        }
    }
}
