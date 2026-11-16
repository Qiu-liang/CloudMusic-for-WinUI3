using System;

namespace music.Services
{
    public sealed class ProgressDragState
    {
        public bool IsDragging { get; private set; }
        public TimeSpan? PendingSeek { get; private set; }

        public void PointerPressed() => IsDragging = true;
        public void ValueChanged(double percentage, TimeSpan duration)
        {
            if (IsDragging)
                PendingSeek = TimeSpan.FromSeconds(Math.Clamp(percentage, 0, 100) * duration.TotalSeconds / 100);
        }

        public TimeSpan? PointerReleased() => Finish(true);
        public TimeSpan? PointerCaptureLost() => Finish(true);

        private TimeSpan? Finish(bool commit)
        {
            if (!IsDragging) return null;
            IsDragging = false;
            var position = PendingSeek;
            PendingSeek = null;
            return commit ? position : null;
        }
    }
}
