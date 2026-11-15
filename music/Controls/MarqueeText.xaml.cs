using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using music.Services;

namespace music.Controls
{
    public sealed partial class MarqueeText : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(MarqueeText),
                new PropertyMetadata(string.Empty, OnTextChanged));

        public string Text
        {
            get => (string)(GetValue(TextProperty) ?? string.Empty);
            set => SetValue(TextProperty, value ?? string.Empty);
        }

        public double Gap { get; set; } = MarqueeLayoutPolicy.DefaultGap;
        public double SpeedPxPerSecond { get; set; } = MarqueeLayoutPolicy.DefaultSpeedPxPerSecond;
        public double InitialPauseSeconds { get; set; } = MarqueeLayoutPolicy.DefaultInitialPauseSeconds;

        private Storyboard? _storyboard;
        private bool _isHovered;

        public MarqueeText()
        {
            InitializeComponent();
            Loaded += MarqueeText_Loaded;
            Unloaded += MarqueeText_Unloaded;

            RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => UpdateMarquee());
            RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => SyncTextProperties());
            RegisterPropertyChangedCallback(FontFamilyProperty, (_, _) => UpdateMarquee());
            RegisterPropertyChangedCallback(FontWeightProperty, (_, _) => UpdateMarquee());
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueeText control)
            {
                control.UpdateMarquee();
            }
        }

        private void MarqueeText_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMarquee();
        }

        private void MarqueeText_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void ClipHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateClip(e.NewSize);
            UpdateMarquee();
        }

        private void ClipHost_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = true;
            _storyboard?.Pause();
        }

        private void ClipHost_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _isHovered = false;
            _storyboard?.Resume();
        }

        private void SyncTextProperties()
        {
            var text = Text;
            PrimaryText.Text = text;
            SecondaryText.Text = text;
            ScrollTrack.Spacing = Gap;

            ApplyStyleTo(PrimaryText);
            ApplyStyleTo(SecondaryText);
        }

        private void ApplyStyleTo(TextBlock target)
        {
            target.FontSize = FontSize;
            target.FontWeight = FontWeight;
            if (FontFamily != null) target.FontFamily = FontFamily;
            if (Foreground != null) target.Foreground = Foreground;
        }

        private void UpdateClip(Size size)
        {
            if (size.Width > 0 && size.Height > 0)
            {
                ClipHost.Clip = new RectangleGeometry
                {
                    Rect = new Rect(0, 0, size.Width, size.Height)
                };
            }
            else
            {
                ClipHost.Clip = null;
            }
        }

        private void UpdateMarquee()
        {
            SyncTextProperties();

            var text = Text;
            // Measure with a non-empty fallback character when Text is empty so single-line height stays stable.
            if (string.IsNullOrEmpty(text))
            {
                PrimaryText.Text = " ";
            }

            PrimaryText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = string.IsNullOrEmpty(text) ? 0 : PrimaryText.DesiredSize.Width;
            var lineHeight = Math.Max(16, Math.Ceiling(PrimaryText.DesiredSize.Height));

            if (string.IsNullOrEmpty(text))
            {
                PrimaryText.Text = string.Empty;
            }

            ClipHost.Height = lineHeight;
            TextCanvas.Height = lineHeight;

            var viewportWidth = ClipHost.ActualWidth;
            UpdateClip(new Size(viewportWidth, lineHeight));

            var metrics = MarqueeLayoutPolicy.ComputeMetrics(
                textWidth,
                viewportWidth,
                Gap,
                SpeedPxPerSecond,
                InitialPauseSeconds);

            StopAnimation();

            if (!metrics.ShouldScroll)
            {
                SecondaryText.Visibility = Visibility.Collapsed;
                ToolTipService.SetToolTip(ClipHost, null);
                return;
            }

            SecondaryText.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(ClipHost, text);

            if (!IsLoaded)
            {
                return;
            }

            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),
                Value = 0
            });
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(metrics.InitialPause),
                Value = 0
            });
            animation.KeyFrames.Add(new LinearDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(metrics.TotalDuration),
                Value = -metrics.TravelDistance
            });

            Storyboard.SetTarget(animation, TrackTransform);
            Storyboard.SetTargetProperty(animation, "X");

            var storyboard = new Storyboard
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            storyboard.Children.Add(animation);

            _storyboard = storyboard;
            storyboard.Begin();
            if (_isHovered)
            {
                storyboard.Pause();
            }
        }

        private void StopAnimation()
        {
            if (_storyboard != null)
            {
                _storyboard.Stop();
                _storyboard.Children.Clear();
                _storyboard = null;
            }

            TrackTransform.X = 0;
        }
    }
}
