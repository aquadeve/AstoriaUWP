using AndroidInteropLib.android.content;

namespace AndroidInteropLib.android.support.design.widget
{
    public class BadgeDrawable
    {
        private int number;
        private bool visible = true;

        private BadgeDrawable() { }

        public static BadgeDrawable create(Context context)
        {
            return new BadgeDrawable();
        }

        public void setNumber(int number)
        {
            this.number = number;
        }

        public int getNumber() { return number; }

        public void setVisible(bool visible)
        {
            this.visible = visible;
        }

        public bool isVisible() { return visible; }

        public void clearNumber()
        {
            this.number = 0;
        }
    }
}
