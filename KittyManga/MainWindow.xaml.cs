﻿//Copyright (c) 2018 Chi Cheng Hsu
//MIT License

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Configuration;

namespace KittyManga {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public const int NUM_CH_COL = 5;
        public const string USER_DATA_FILE = "UserData.xml";
        MangaAPI api = new MangaAPI();
        Thread searchThread = null, mangaFetchThread = null, mangaPrefetchThread = null;

        public static RoutedCommand ToggleFullscreenCommand = new RoutedCommand("ToggleFullscreenCommand", typeof(MainWindow), new InputGestureCollection() { new KeyGesture(Key.F11) });
        public static RoutedCommand EscCommand = new RoutedCommand("EscCommand", typeof(MainWindow), new InputGestureCollection() { new KeyGesture(Key.Escape) });


        Manga curManga = null;
        int curChIndex = -1;
        BitmapImage[] prefetchedData = null;
        bool loadPrefetchOnComplete = false;
        bool hasPrev, hasNext;

        Dictionary<string, int> bookmarks;
        List<string> recents = new List<string>();
        bool nightmode = false;

        public MainWindow() {
            InitializeComponent();
            SearchPane.Visibility = DisplayPane.Visibility = Visibility.Hidden;
            this.DataContext = this;
            AsyncInit();

            //Init buttons for suggestions
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(SugButtons); i++) {
                Button butt = (VisualTreeHelper.GetChild(SugButtons, i) as Button);
                butt.Resources.Add("MangaIndex", -1);
                butt.Visibility = Visibility.Hidden;
                butt.Click += (s, e) => {
                    for (int j = 0; j < VisualTreeHelper.GetChildrenCount(SugButtons); j++)
                        (VisualTreeHelper.GetChild(SugButtons, j) as Button).Background = Brushes.Transparent;
                    (s as Button).Background = Brushes.DarkGoldenrod;
                    AsyncFetchManga((int)(s as Button).Resources["MangaIndex"]);
                };
            }

            //Add columns for chapters
            for (int i = 0; i < NUM_CH_COL; i++)
                ChGrid.ColumnDefinitions.Add(new ColumnDefinition());

            //Start timers
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval += TimeSpan.FromMilliseconds(500);
            timer.Tick += (s, e) => { AsyncSearch(s, null); };
            timer.Start();

            string setting;
            if ((setting = ReadSetting("NightMode")) != null)
                NightMode = bool.Parse(setting);
            if ((setting = ReadSetting("LeftToRight")) != null)
                LeftToRight = bool.Parse(setting);

            Application.Current.Exit += OnExit;
        }

        #region Asynchronous Fetching

        string lastSearch = "";
        public void AsyncSearch(object s, KeyEventArgs args) {
            if (args != null && args.Key != Key.Return)
                return;
            if (s is DispatcherTimer && lastSearch == SearchBar.Text)
                return;
            if (searchThread != null)
                searchThread.Abort();

            lastSearch = SearchBar.Text;
            searchThread = null;
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.DoWork += (sender, e) => {
                try {
                    searchThread = Thread.CurrentThread;
                    e.Result = api.SearchIndex(e.Argument as string, 5);
                }
                catch (ThreadAbortException) {
                    e.Cancel = true;
                    Thread.ResetAbort();
                    searchThread = null;
                }
            };
            worker.RunWorkerCompleted += (sender, e) => {
                if (e.Cancelled)
                    return;
                searchThread = null;
                List<MangaAPI.MangaMatch> sug = e.Result as List<MangaAPI.MangaMatch>;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(SugButtons); i++) {
                    Button butt = (VisualTreeHelper.GetChild(SugButtons, i) as Button);
                    butt.Visibility = Visibility.Visible;
                    int index = sug[i].index;
                    butt.Content = $"{api.mainIndex.manga[index].t} ({(sug[i].s * 100).ToString("0.#")}%)";
                    butt.Resources["MangaIndex"] = index;
                }
                foreach (object obj in SugButtons.Children)
                    (obj as Button).Background = null;

                if (args != null && args.Key == Key.Return)
                    (VisualTreeHelper.GetChild(SugButtons, 0) as Button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            worker.RunWorkerAsync(SearchBar.Text);
        }


        bool fetchingChapter = false;
        public void AsyncFetchChapter(Manga m, int chIndex) {
            if (fetchingChapter)
                return;
            fetchingChapter = true;

            if (mangaPrefetchThread != null)
                mangaPrefetchThread.Abort();
            mangaPrefetchThread = null;
            prefetchedData = null;

            ImagePane.Children.Clear();
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += (s, e) => {
                e.Result = api.FetchChapter(e.Argument as string, worker);
            };
            worker.WorkerReportsProgress = true;
            worker.ProgressChanged += (s, e) => {
                ProgressTip = $"Fetching {e.ProgressPercentage}";
            };
            worker.RunWorkerCompleted += (s, e) => {
                LoadChapterFromData(e.Result as BitmapImage[], m, chIndex);
                loadPrefetchOnComplete = false;
                ProgressTip = "Idle";
                fetchingChapter = false;
            };
            worker.RunWorkerAsync(m.chapters[chIndex][3] as string);
        }

        public void AsyncPrefetchChapter(Manga m, int chIndex) {
            if (mangaPrefetchThread != null)
                mangaPrefetchThread.Abort();
            mangaPrefetchThread = null;
            prefetchedData = null;

            BackgroundWorker worker = new BackgroundWorker();
            ProgressTip = $"Pre-Fetching...";
            worker.DoWork += (sender, e) => {
                try {
                    mangaPrefetchThread = Thread.CurrentThread;
                    e.Result = api.FetchChapter(e.Argument as string, worker);
                }
                catch (ThreadAbortException) {
                    e.Cancel = true;
                    Thread.ResetAbort();
                    mangaPrefetchThread = null;
                }
            };
            worker.WorkerReportsProgress = true;
            worker.ProgressChanged += (s, e) => {
                ProgressTip = $"Pre-Fetching {e.ProgressPercentage}";
            };
            worker.RunWorkerCompleted += (sender, e) => {
                if (e.Cancelled)
                    return;
                ProgressTip = "Idle";
                prefetchedData = e.Result as BitmapImage[];
                if (LeftToRight)
                    RightButt.ToolTip = "Next";
                else
                    LeftButt.ToolTip = "Next";
                if (loadPrefetchOnComplete)
                    LoadChapterFromData(prefetchedData, m, chIndex);

                mangaPrefetchThread = null;
            };
            worker.RunWorkerAsync(m.chapters[chIndex][3] as string);
        }

        class MangaImage {
            public BitmapImage cover = null;
            public Manga m;
        }

        public void AsyncFetchManga(int index) {
            if (mangaFetchThread != null)
                mangaFetchThread.Abort();
            mangaFetchThread = null;
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.DoWork += (sender, e) => {
                try {
                    mangaFetchThread = Thread.CurrentThread;
                    MangaImage r = new MangaImage();
                    r.m = api.FetchManga(index);
                    if (r.m.image != null)
                        r.cover = api.FetchCover(r.m);
                    e.Result = r;
                }
                catch (ThreadAbortException) {
                    e.Cancel = true;
                    Thread.ResetAbort();
                    mangaFetchThread = null;
                }
            };
            worker.RunWorkerCompleted += (sender, e) => {
                if (e.Cancelled)
                    return;
                mangaFetchThread = null;
                MangaImage r = e.Result as MangaImage;
                MangaDesc.Text = r.m.description;
                MangaCover.Visibility = r.cover == null ? Visibility.Hidden : Visibility.Visible;
                (MangaCover.Children[0] as Image).Source = r.cover;
                string info = $"Title: {r.m.title}\nAuthor: {r.m.author}\nCat: ";
                for (int i = 0; i < r.m.categories.Length - 1; i++) {
                    info += r.m.categories[i];
                    info += ", ";
                }
                if (r.m.categories.Length > 0)
                    info += r.m.categories[r.m.categories.Length - 1];
                info += $"\nReleased: {r.m.released}\nHits: {r.m.hits.ToString("N0")}";
                MangaInfo.Text = info;
                for (int i = ChGrid.RowDefinitions.Count; i < r.m.chapters.Length / NUM_CH_COL + 1; i++)
                    ChGrid.RowDefinitions.Add(new RowDefinition());
                ChGrid.Children.Clear();
                Thickness thicc = new Thickness(1, 1, 1, 1);
                for (int i = 0; i < r.m.chapters.Length; i++) {
                    Button butt = new Button();
                    butt.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                    string n = api.GetChapterName(r.m, i);
                    butt.Content = n;
                    butt.ToolTip = n + " " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((double)r.m.chapters[i][1]).ToLocalTime().ToString();
                    Grid.SetColumn(butt, i % NUM_CH_COL);
                    Grid.SetRow(butt, i / NUM_CH_COL);
                    butt.Margin = thicc;
                    butt.Resources.Add("i", i);
                    butt.Resources.Add("m", r.m);
                    butt.HorizontalAlignment = HorizontalAlignment.Stretch;
                    butt.Click += (s, eh) => {
                        SearchPane.Visibility = Visibility.Hidden;
                        AsyncFetchChapter((s as Button).Resources["m"] as Manga, (int)(s as Button).Resources["i"]);
                    };
                    ChGrid.Children.Add(butt);
                }
                if (bookmarks.ContainsKey(r.m.id) && ChGrid.Children.Count > bookmarks[r.m.id])
                    (ChGrid.Children[bookmarks[r.m.id]] as Button).Background = Brushes.DarkGoldenrod;
            };
            worker.RunWorkerAsync();
        }

        public void AsyncInit() {
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.DoWork += (sender, e) => {
                api.FetchIndex();
                api.FetchManga(0);//Test fetch a manga to check for server connection
                try {
                    using (Stream f = File.OpenRead(USER_DATA_FILE)) {
                        XDocument doc = XDocument.Load(f);
                        recents = doc.Descendants("UserData").Descendants("recents").Select(x => (string)x.Attribute("m")).ToList();
                        bookmarks = doc.Descendants("UserData").Descendants("bookmarks").ToDictionary(x => {
                            if (x.Attribute("k") == null)
                                return "";
                            return (string)x.Attribute("k");
                        }, x => {
                            if (x.Attribute("v") == null)
                                return -1;
                            return (int)x.Attribute("v");
                        });
                    }
                }
                catch (Exception) { bookmarks = new Dictionary<string, int>(); recents = new List<string>(); }
            };
            worker.RunWorkerCompleted += (sender, e) => { SearchPane.Visibility = DisplayPane.Visibility = Visibility.Visible; ProgressTip = "Idle"; };
            worker.RunWorkerAsync();
        }

        #endregion

        #region Image Zooming

        Image zoomedImg = null;
        public void ZoomEnter(object sender, MouseEventArgs args) {
            if (sender is Image) {
                ZoomImg.Source = (sender as Image).Source;
                zoomedImg = sender as Image;
            }
        }

        public void ZoomStart(object sender, MouseButtonEventArgs args) {
            if (zoomedImg != null && args.ChangedButton != MouseButton.Left)
                if (ZoomBg.Visibility == Visibility.Hidden) {
                    Point p = args.GetPosition(zoomedImg as Image);
                    ZoomTo(p.X, p.Y);
                    BarTrigger.IsHitTestVisible = false;
                    ZoomBg.Visibility = Visibility.Visible;
                }
                else ZoomEnd(sender, args);
        }

        public void ZoomEnd(object sender, MouseButtonEventArgs args) {
            if (sender is Image && ZoomBg.Visibility == Visibility.Visible) {
                ZoomBg.Visibility = Visibility.Hidden;
                BarTrigger.IsHitTestVisible = true;
            }
        }

        public void ZoomMove(object sender, MouseEventArgs args) {
            if (sender is Image && ZoomBg.Visibility == Visibility.Visible) {
                Point p = args.GetPosition(sender as Image);
                ZoomTo(p.X, p.Y);
            }
        }

        public void ZoomTo(double x, double y) {
            ScaleTransform transform = (ZoomImg.RenderTransform as TransformGroup).Children[0] as ScaleTransform;
            transform.CenterX = x;
            transform.CenterY = y;
        }

        #endregion

        #region UI

        public void LoadChapterFromData(BitmapImage[] data, Manga m, int chIndex) {
            prefetchedData = null;
            loadPrefetchOnComplete = false;

            if (bookmarks.ContainsKey(m.id)) {
                if (curManga != m && ChGrid.Children.Count > bookmarks[m.id])
                    (ChGrid.Children[bookmarks[m.id]] as Button).Background = null;
                bookmarks[m.id] = chIndex;
            }
            else
                bookmarks.Add(m.id, chIndex);

            Binding binding = new Binding();
            binding.Path = new PropertyPath("ViewportWidth");
            binding.Source = DisplayScroll;
            ImagePane.Children.Clear();
            for (int i = 0; i < data.Length; i++) {
                Image img = new Image();
                img.MouseDown += ZoomStart;
                img.MouseRightButtonUp += ZoomEnd;
                img.MouseEnter += ZoomEnter;
                img.MouseMove += ZoomMove;
                img.Source = data[i];
                BindingOperations.SetBinding(img, Image.MaxWidthProperty, binding);
                ImagePane.Children.Add(img);
            }

            if (ChGrid.Children.Count > chIndex)
                if (curManga == null)
                    (ChGrid.Children[chIndex] as Button).Background = Brushes.DarkGoldenrod;
                else if (curManga == m) {
                    (ChGrid.Children[curChIndex] as Button).Background = null;
                    (ChGrid.Children[chIndex] as Button).Background = Brushes.DarkGoldenrod;
                }
            curManga = m;
            curChIndex = chIndex;

            BarChName.Text = m.title + ", " + api.GetChapterName(m, chIndex);
            hasPrev = chIndex > 0;
            hasNext = chIndex < m.chapters.Length - 1;

            if (LeftToRight) {
                LeftButt.Visibility = hasPrev ? Visibility.Visible : Visibility.Hidden;
                RightButt.Visibility = hasNext ? Visibility.Visible : Visibility.Hidden;
                DisplayScroll.ScrollToLeftEnd();
                LeftButt.ToolTip = "Prev";
                RightButt.ToolTip = "Next (Fetching...)";
            }
            else {
                LeftButt.Visibility = hasNext ? Visibility.Visible : Visibility.Hidden;
                RightButt.Visibility = hasPrev ? Visibility.Visible : Visibility.Hidden;
                DisplayScroll.ScrollToRightEnd();
                RightButt.ToolTip = "Prev";
                LeftButt.ToolTip = "Next (Fetching...)";
            }

            if (hasNext)
                AsyncPrefetchChapter(m, chIndex + 1);

            GC.Collect();
        }

        public string ProgressTip {
            set {
                BarProgessText.Text = ProgessText.Text = value;
            }
        }

        public void ScrollHorizontal(object sender, MouseWheelEventArgs args) {
            if (sender is ScrollViewer) {
                if (args.Delta > 0)
                    for (int i = 0; i < args.Delta; i++) (sender as ScrollViewer).LineLeft();
                else
                    for (int i = 0; i < -args.Delta; i++) (sender as ScrollViewer).LineRight();
                args.Handled = true;
            }
        }

        public void ToggleSearchPane(object sender, RoutedEventArgs e) {
            SearchPane.Visibility = SearchPane.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
        }


        public void LeftButtSlapped(object sender, RoutedEventArgs e) {
            if (LeftToRight)
                LoadPrev();
            else
                LoadNext();
        }

        public void RightButtSlapped(object sender, RoutedEventArgs e) {
            if (LeftToRight)
                LoadNext();
            else
                LoadPrev();
        }

        public void LoadNext() {
            if (prefetchedData == null)
                loadPrefetchOnComplete = true;
            else
                LoadChapterFromData(prefetchedData, curManga, curChIndex + 1);
        }

        public void LoadPrev() {
            AsyncFetchChapter(curManga, curChIndex - 1);
        }

        public bool LeftToRight {
            set {
                ImagePane.FlowDirection = value ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
                DisplayScroll.ScrollToHorizontalOffset(DisplayScroll.ScrollableWidth - DisplayScroll.HorizontalOffset);
                if (value) {
                    DisplayScroll.ScrollSpeed = Math.Abs(DisplayScroll.ScrollSpeed);
                    LeftButt.Visibility = hasPrev ? Visibility.Visible : Visibility.Hidden;
                    RightButt.Visibility = hasNext ? Visibility.Visible : Visibility.Hidden;
                    DisplayScroll.ScrollToLeftEnd();
                    LeftButt.ToolTip = "Prev";
                    RightButt.ToolTip = prefetchedData == null ? "Next (Fetching...)" : "Next";
                }
                else {
                    DisplayScroll.ScrollSpeed = -Math.Abs(DisplayScroll.ScrollSpeed);
                    LeftButt.Visibility = hasNext ? Visibility.Visible : Visibility.Hidden;
                    RightButt.Visibility = hasPrev ? Visibility.Visible : Visibility.Hidden;
                    DisplayScroll.ScrollToRightEnd();
                    RightButt.ToolTip = "Prev";
                    LeftButt.ToolTip = prefetchedData == null ? "Next (Fetching...)" : "Next";
                }
            }
            get {
                return ImagePane.FlowDirection == FlowDirection.LeftToRight;
            }
        }

        public bool NightMode {
            set {
                nightmode = value;
                if (nightmode)
                    DisplayScroll.Effect = new InvertEffect();
                else
                    DisplayScroll.Effect = null;
            }
            get { return nightmode; }
        }

        public bool Fullscreen {
            set {
                if (value) {
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                }
                else {
                    WindowStyle = WindowStyle.SingleBorderWindow;
                    WindowState = WindowState.Normal;
                }
            }
            get { return WindowStyle == WindowStyle.None; }
        }

        public void ToggleNightMode(object sender, RoutedEventArgs e) {
            NightMode = !NightMode;
            AddUpdateAppSettings("NightMode", NightMode.ToString());
        }

        public void ToggleReadDirection(object sender, RoutedEventArgs e) {
            LeftToRight = !LeftToRight;
            AddUpdateAppSettings("LeftToRight", LeftToRight.ToString());
        }

        public void ToggleFullscreen(object sender, ExecutedRoutedEventArgs e) {
            Fullscreen = !Fullscreen;
        }

        public void EscPressed(object sender, ExecutedRoutedEventArgs e) {
            if (SearchPane.Visibility == Visibility.Visible)
                SearchPane.Visibility = Visibility.Hidden;
            else if (Fullscreen)
                Fullscreen = false;
        }

        #endregion

        #region Settings management

        static string ReadSetting(string key) {
            try {
                var appSettings = ConfigurationManager.AppSettings;
                return appSettings[key] ?? null;
            }
            catch (ConfigurationErrorsException) {
                return null;
            }
        }

        static void AddUpdateAppSettings(string key, string value) {
            try {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[key] == null)
                    settings.Add(key, value);
                else
                    settings[key].Value = value;
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException) { }
        }

        public void OnExit(object sender, ExitEventArgs args) {
            new Thread(() => {
                XDocument doc = new XDocument();
                XElement root = new XElement("UserData");
                XElement xElem = new XElement(
                        "bookmarks",
                        bookmarks.Select(x => { if (x.Key == "") return null; return new XElement("bookmarks", new XAttribute("k", x.Key), new XAttribute("v", x.Value)); })
                     );
                root.Add(xElem);
                xElem = new XElement("recents", recents.Select(x => { if (x == null) return null; return new XElement("recents", new XAttribute("m", x)); }));
                root.Add(xElem);
                doc.Add(root);
                doc.Save(USER_DATA_FILE);
            }).Start();
        }

        #endregion
    }
}
