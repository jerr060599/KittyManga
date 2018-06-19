//Copyright (c) 2018 Chi Cheng Hsu
//MIT License

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KittyManga {
    /// <summary>
    /// A custom control that is built for viewing images laid out horizontally
    /// The main difference from ScrollViewer is that this implements smooht scrolling, which is important for reading while scrolling.
    /// This can only scroll horizontally
    /// </summary>
    class SmoothScrollViewer : ScrollViewer {
        /// <summary>
        /// The target scroll speed in pixels per second per mouse wheel delta
        /// </summary>
        public static readonly DependencyProperty ScrollSpeedProperty =
            DependencyProperty.Register("ScrollSpeed", typeof(double), typeof(SmoothScrollViewer), new FrameworkPropertyMetadata(3.0));
        /// <summary>
        /// How much time in seconds the motion is interpolated.
        /// </summary>
        public static readonly DependencyProperty SmoothnessProperty =
            DependencyProperty.Register("Smoothness", typeof(double), typeof(SmoothScrollViewer), new FrameworkPropertyMetadata(0.3));

        /// <summary>
        /// Invert mouse wheel direction?
        /// </summary>
        public static readonly DependencyProperty InvertMouseWheelProperty =
            DependencyProperty.Register("InvertMouseWheel", typeof(bool), typeof(SmoothScrollViewer), new FrameworkPropertyMetadata(false));

        /// <summary>
        /// The target scroll speed in pixels per second per mouse wheel delta
        /// </summary>
        public double ScrollSpeed {
            get { return (double)GetValue(ScrollSpeedProperty); }
            set { SetValue(ScrollSpeedProperty, value); }
        }

        /// <summary>
        /// How much time in seconds the motion is interpolated.
        /// </summary>
        public double Smoothness {
            get { return (double)GetValue(SmoothnessProperty); }
            set { SetValue(SmoothnessProperty, value); }
        }

        /// <summary>
        /// Invert mouse wheel direction?
        /// </summary>
        public bool InvertMouseWheel {
            get { return (bool)GetValue(InvertMouseWheelProperty); }
            set { SetValue(InvertMouseWheelProperty, value); }
        }

        double buffer = 0;
        public const double FPS = 60;
        double deadline;
        FpsTicker ticker;

        public SmoothScrollViewer() : base() {
            ticker = new FpsTicker();
            ticker.FPS = FPS;
            ticker.Tick = ScrollTick;
            PreviewMouseWheel += OnPreviewMouseWheel;
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewKeyUp += OnPreviewKeyUp;
            Loaded += (s, e) => { ticker.Start(); };
            Unloaded += (s, e) => { ticker.Stop(); };
            SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Hidden);
            SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        }

        void OnPreviewMouseWheel(object sender, MouseWheelEventArgs args) {
            if (InvertMouseWheel)
                buffer += args.Delta * ScrollSpeed;
            else
                buffer -= args.Delta * ScrollSpeed;

            deadline = Smoothness;
            args.Handled = true;
        }

        double constantScroll = 0;
        void OnPreviewKeyDown(object sender, KeyEventArgs args) {
            if (args.Key == Key.Right && constantScroll == 0) {
                constantScroll = ScrollSpeed / Smoothness * 100;
                buffer += ScrollSpeed * 100;
                deadline = Smoothness;
                args.Handled = true;
            }
            if (args.Key == Key.Left && constantScroll == 0) {
                constantScroll = -ScrollSpeed / Smoothness * 100;
                buffer -= ScrollSpeed * 100;
                deadline = Smoothness;
                args.Handled = true;
            }
        }

        void OnPreviewKeyUp(object sender, KeyEventArgs args) {
            if (args.Key == Key.Left || args.Key == Key.Right) {
                constantScroll = 0;
                args.Handled = true;
            }
        }

        void ScrollTick(double deltaTime) {
            /*
             Scroll animation has two values, buffer, and deadline
             The goal of the animation is to smoothly move the scroll by buffer before the deadline.
             It is made so that buffer is "used up" and transfered to scrolling exactly when deadline becomes 0.

             constantScroll simply adds a linear velocity when the animation is complete for if the user scrolls by holding arrow keys.
             */
            if (deadline <= deltaTime) {
                if (buffer + constantScroll * deltaTime != 0) {
                    ScrollToHorizontalOffset(HorizontalOffset + buffer + constantScroll * deltaTime);
                    UpdateLayout();
                }
                deadline = buffer = 0;
            }
            else {
                double delta = buffer / deadline * deltaTime;
                ScrollToHorizontalOffset(HorizontalOffset + delta);
                UpdateLayout();
                buffer -= delta;
                deadline -= deltaTime;
            }
        }
    }
}
