namespace AndroidInteropLib.android.support.design.widget
{
    public class BottomSheetBehavior
    {
        public const int STATE_DRAGGING = 1;
        public const int STATE_SETTLING = 2;
        public const int STATE_EXPANDED = 3;
        public const int STATE_COLLAPSED = 4;
        public const int STATE_HIDDEN = 5;
        public const int STATE_HALF_EXPANDED = 6;

        private int state = STATE_COLLAPSED;
        private int peekHeight;
        private bool hideable;

        public void setState(int state)
        {
            this.state = state;
        }

        public int getState() { return state; }

        public void setPeekHeight(int height)
        {
            this.peekHeight = height;
        }

        public int getPeekHeight() { return peekHeight; }

        public void setHideable(bool hideable)
        {
            this.hideable = hideable;
        }

        public bool isHideable() { return hideable; }
    }
}
