//Copyright (c) 2018 Chi Cheng Hsu
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
        public const int NUM_RECENTS_COL = 5;
        public const int NUM_UPDATES_COL = 5;
        public const int NUM_UPDATES_ROW = 8;

        public const string USER_DATA_FILE = "UserData.xml";
        MangaAPI api = new MangaAPI();
        Thread searchThread = null, fetchMangaThread = null, mangaPrefetchThread = null, fetchUpdateThread = null, fetchRecentThread = null;

        public static RoutedCommand ToggleFullscreenCommand = new RoutedCommand("ToggleFullscreenCommand", typeof(MainWindow), new InputGestureCollection() { new KeyGesture(Key.F11) });
        public static RoutedCommand EscCommand = new RoutedCommand("EscCommand", typeof(MainWindow), new InputGestureCollection() { new KeyGesture(Key.Escape) });

        Manga curManga = null;
        int curChIndex = -1;
        BitmapImage[] prefetchedData = null;
        bool loadPrefetchOnComplete = false;
        bool hasPrev, hasNext;

        Dictionary<string, MangaBookmark> bookmarks;
        bool nightmode = false;

        public MainWindow() {
            InitializeComponent();
            SearchPane.Visibility = DisplayPane.Visibility = Visibility.Hidden;
            this.DataContext = this;
            AsyncInit();

            //Init buttons for suggestions
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(SugButtons); i++) {
                Button butt = (VisualTreeHelper.GetChild(SugButtons, i) as Button);
                butt.Resources.Add("MangaId", -1);
                butt.Visibility = Visibility.Hidden;
                butt.Click += (s, e) => {
                    for (int j = 0; j < VisualTreeHelper.GetChildrenCount(SugButtons); j++)
                        (VisualTreeHelper.GetChild(SugButtons, j) as Button).Background = Brushes.Transparent;
                    (s as Button).Background = Brushes.DarkGoldenrod;
                    AsyncFetchManga((string)(s as Button).Resources["MangaId"]);
                };
            }

            //Add columns, rows and buttons for grids
            for (int i = 0; i < NUM_CH_COL; i++)
                ChGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < NUM_RECENTS_COL; i++) {
                RecentsGrid.ColumnDefinitions.Add(new ColumnDefinition());
                Grid g = BuildCoverButton();
                Grid.SetColumn(g, i);
                RecentsGrid.Children.Add(g);
            }

            for (int i = 0; i < NUM_UPDATES_COL; i++)
                UpdatesGrid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < NUM_UPDATES_ROW; i++) {
                UpdatesGrid.RowDefinitions.Add(new RowDefinition());
                for (int j = 0; j < NUM_UPDATES_COL; j++) {
                    Grid g = BuildCoverButton();
                    Grid.SetRow(g, i);
                    Grid.SetColumn(g, j);

                    UpdatesGrid.Children.Add(g);
                }
            }

            //Start the timer for searching.
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval += TimeSpan.FromMilliseconds(500);
            timer.Tick += (s, e) => { AsyncSearch(s, null); };
            timer.Start();

            //Read settings
            string setting;
            if ((setting = ReadSetting("NightMode")) != null)
                NightMode = bool.Parse(setting);
            if ((setting = ReadSetting("LeftToRight")) != null)
                LeftToRight = bool.Parse(setting);

            //Subscribe to exit to save settings
            Application.Current.Exit += OnExit;
        }

        #region Asynchronous Fetching

        string lastSearch = "";
        public void AsyncSearch(object s, KeyEventArgs args) {
            if (api.mainIndex == null) return;
            if (args != null && args.Key != Key.Return)
                return;//Search on enter or if called by the timer
            if (s is DispatcherTimer && lastSearch == SearchBar.Text)
                return;//Dont search if the last search term didnt change
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
                //Update the buttons with the results and reset them
                List<MangaAPI.MangaMatch> sug = e.Result as List<MangaAPI.MangaMatch>;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(SugButtons); i++) {
                    Button butt = (VisualTreeHelper.GetChild(SugButtons, i) as Button);
                    butt.Visibility = Visibility.Visible;
                    int index = sug[i].index;
                    butt.Content = $"{api[index].t} ({(sug[i].s * 100).ToString("0.#")}%)";
                    butt.Resources["MangaId"] = api[index].i;
                    butt.Background = null;
                }

                if (args != null && args.Key == Key.Return)//Highlight the first result if enter is pressed
                    (VisualTreeHelper.GetChild(SugButtons, 0) as Button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            worker.RunWorkerAsync(SearchBar.Text);
        }

        bool fetchingChapter = false;
        public void AsyncFetchChapter(Manga m, int chIndex) {
            if (fetchingChapter)//Can only fetch one cahpter at a time
                return;
            fetchingChapter = true;

            //If prefetching, abort it cause were jumping to a new chapter
            if (mangaPrefetchThread != null)
                mangaPrefetchThread.Abort();
            mangaPrefetchThread = null;
            prefetchedData = null;

            ImagePane.Children.Clear();
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += (s, e) => {//Fetch
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
                AsyncFetchRecents();//Update recents panel
            };
            worker.RunWorkerAsync(m.chapters[chIndex][3] as string);
        }

        //Fetch a chapter but keep it in memory and not load it
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
                if (loadPrefetchOnComplete)//If the next button is clicked, this is set to true so it loads after its done fetching
                    LoadChapterFromData(prefetchedData, m, chIndex);

                mangaPrefetchThread = null;
            };
            worker.RunWorkerAsync(m.chapters[chIndex][3] as string);
        }

        //A quick class to pass data from DoWork To Complete in AsyncFetchManga
        class MangaImage {
            public BitmapImage cover = null;
            public Manga m;
        }

        //Fetches details for a manga and loads it up on the mangainfopane
        public void AsyncFetchManga(string id) {
            if (api.mainIndex == null) return;
            if (fetchMangaThread != null)
                fetchMangaThread.Abort();
            fetchMangaThread = null;
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.DoWork += (sender, e) => {
                try {
                    fetchMangaThread = Thread.CurrentThread;
                    MangaImage r = new MangaImage();
                    r.m = api.FetchManga(id);
                    if (r.m.image != null)
                        r.cover = api.FetchCover(r.m);
                    e.Result = r;
                }
                catch (ThreadAbortException) {
                    e.Cancel = true;
                    Thread.ResetAbort();
                    fetchMangaThread = null;
                }
            };
            worker.RunWorkerCompleted += (sender, e) => {
                if (e.Cancelled)
                    return;
                fetchMangaThread = null;
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
                    ChGrid.RowDefinitions.Add(new RowDefinition());//Added extra rows if needed
                ChGrid.Children.Clear();
                Thickness thicc = new Thickness(1, 1, 1, 1);
                for (int i = 0; i < r.m.chapters.Length; i++) {//Add all the chapter buttons
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
                }//Update button background according to bookmark
                if (bookmarks.ContainsKey(r.m.id) && ChGrid.Children.Count > bookmarks[r.m.id].lastChapter)
                    (ChGrid.Children[bookmarks[r.m.id].lastChapter] as Button).Background = Brushes.DarkGoldenrod;
                MangaInfoPane.Visibility = Visibility.Visible;
                MangaInfoPane.Height = double.NaN;
            };
            worker.RunWorkerAsync();
        }

        public void AsyncInit() {
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            worker.DoWork += (sender, e) => {
                api.FetchIndex(true);//Fetches the mainIndex from a file or the internet if the cached file is too old
                try {//Try to load the user configs and bookmarks
                    using (Stream f = File.OpenRead(USER_DATA_FILE)) {
                        XDocument doc = XDocument.Load(f);
                        bookmarks = doc.Descendants("UserData").Descendants("bookmarks").ToDictionary(x => {
                            if (x.Attribute("k") == null)
                                return "";
                            return (string)x.Attribute("k");
                        }, x => {
                            if (x.Attribute("k") == null)
                                return null;
                            return new MangaBookmark() { lastChapter = (int)x.Attribute("v"), id = (string)x.Attribute("k"), lastRead = DateTime.Parse((string)x.Attribute("d")) };
                        });
                    }
                }//Any goes wrong, just reset all bookmarks
                catch (Exception) { bookmarks = new Dictionary<string, MangaBookmark>(); }
            };
            worker.RunWorkerCompleted += (sender, e) => {
                SearchPane.Visibility = DisplayPane.Visibility = Visibility.Visible; ProgressTip = "Idle";
                AsyncFetchUpdates();
                AsyncFetchRecents();
            };
            worker.RunWorkerAsync();
        }

        public void AsyncFetchRecents() {
            if (api.mainIndex == null) return;
            if (fetchRecentThread != null) return;
            fetchRecentThread = new Thread(() => {
                Heap<MangaBookmark> heap = new Heap<MangaBookmark>((a, b) => a.lastRead.CompareTo(b.lastRead));
                //Find most recently read mangas from bookmakrs
                foreach (var item in bookmarks) {
                    if (item.Key == "") continue;
                    if (heap.Count < NUM_RECENTS_COL)
                        heap.Add(item.Value);
                    else if (heap.Min.lastRead < item.Value.lastRead) {
                        heap.RemoveMin();
                        heap.Add(item.Value);
                    }
                }
                //Search index for their image links by hash code
                int i = 0;
                foreach (var item in heap) {
                    int hash = item.id.GetHashCode();
                    MangaAddress a = new MangaAddress();
                    bool found = false;
                    string url = null;
                    //Since the recents col is at most five or so, I didnt see the need to init a dict for 10000 mangas
                    foreach (var o in api.mainIndex.manga)
                        if (o.idHash == hash && o.i == item.id) {
                            url = o.im;
                            a = o;
                            found = true;
                            break;
                        }
                    if (!found)
                        continue;
                    BitmapImage cover = null;
                    if (url != null)
                        cover = api.DownloadImage(MangaAPI.API_IMG + url);
                    Application.Current.Dispatcher.Invoke(() => {//Update UI
                        Grid g = RecentsGrid.Children[i] as Grid;
                        if (g.Resources.Contains("i"))
                            g.Resources["i"] = a.i;
                        else
                            g.Resources.Add("i", a.i);
                        Image img = g.Children[0] as Image;
                        img.Source = cover;
                        ((g.Children[1] as Viewbox).Child as TextBlock).Text = a.t + $"\nRead {item.lastRead.ToLocalTime().ToString()}";
                        g.Visibility = Visibility.Visible;
                    });
                    i++;
                }
                fetchRecentThread = null;
            });
            fetchRecentThread.Start();
        }

        //Refreshes the main index and refresh the updated page after
        public void AysncRefreshIndex(object s, RoutedEventArgs args) {
            //Make sure nothing that needs mainIndex is using it. TODO: use actual thread locks to make 100% sure
            if (searchThread != null) return;
            if (fetchMangaThread != null) return;
            if (fetchUpdateThread != null) return;
            if (fetchRecentThread != null) return;
            if (api.mainIndex == null) return;
            api.mainIndex = null;
            refreshButt.Visibility = Visibility.Hidden;
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = false;
            ProgressTip = "Refreshing";
            worker.DoWork += (sender, e) => {
                api.FetchIndex(false);
            };
            worker.RunWorkerCompleted += (sender, e) => {
                ProgressTip = "Idle";
                AsyncFetchUpdates();
            };
            worker.RunWorkerAsync();
        }

        //Fetches the update panel covers
        public void AsyncFetchUpdates() {
            if (fetchUpdateThread != null)
                return;
            fetchUpdateThread = new Thread(() => {//Get all the updates and download their covers
                int[] updated = api.FetchUpdated(NUM_UPDATES_ROW * NUM_UPDATES_COL);
                for (int i = 0; i < updated.Length; i++) {
                    BitmapImage cover = api.FetchCover(updated[i]);
                    Application.Current.Dispatcher.Invoke(() => {
                        UpdatesGrid.Children[i].Visibility = Visibility.Visible;
                        Grid g = UpdatesGrid.Children[i] as Grid;
                        ((g.Children[0] as Image)).Source = cover;
                        if (g.Resources.Contains("i"))
                            g.Resources["i"] = api[updated[i]].i;
                        else
                            g.Resources.Add("i", api[updated[i]].i);
                        Viewbox box = (UpdatesGrid.Children[i] as Grid).Children[1] as Viewbox;
                        (box.Child as TextBlock).Text = api[i].t +
                                "\nUpdated " + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((double)api[updated[i]].ld).ToLocalTime();
                    });
                }
                fetchUpdateThread = null;
                Application.Current.Dispatcher.Invoke(() => refreshButt.Visibility = Visibility.Visible);
            });
            fetchUpdateThread.Start();
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

        /// <summary>
        /// Builds the cover button on hte update and recents panel
        /// </summary>
        /// <returns></returns>
        public Grid BuildCoverButton() {
            Grid g = new Grid();
            g.MouseDown += OnCoverPress;

            Image img = new Image();
            g.Children.Add(img);

            Viewbox box = new Viewbox();
            box.IsHitTestVisible = img.IsHitTestVisible = false;
            TextBlock b = new TextBlock();
            b.Foreground = Brushes.White;
            b.FontSize = 20;
            b.VerticalAlignment = VerticalAlignment.Bottom;
            b.Background = new SolidColorBrush(new Color() { A = 160 });
            box.Child = b;
            g.Children.Add(box);

            g.Visibility = Visibility.Hidden;
            return g;
        }

        public void HideMangaInfoPane(object sender, RoutedEventArgs args) {
            MangaInfoPane.Visibility = Visibility.Hidden;
            MangaInfoPane.Height = 0;
        }

        public void OnCoverPress(object sender, MouseButtonEventArgs args) {
            if ((sender as FrameworkElement).Resources.Contains("i")) {
                AsyncFetchManga((string)(sender as FrameworkElement).Resources["i"]);
                SearchPane.ScrollToTop();
            }
        }

        /// <summary>
        /// Loads a chapter from the date provided
        /// </summary>
        /// <param name="data">The images</param>
        /// <param name="m">The manga it came from</param>
        /// <param name="chIndex">The chapter it came from</param>
        public void LoadChapterFromData(BitmapImage[] data, Manga m, int chIndex) {
            prefetchedData = null;
            loadPrefetchOnComplete = false;

            if (bookmarks.ContainsKey(m.id)) {//Update bookmark buttons
                if (curManga != m && ChGrid.Children.Count > bookmarks[m.id].lastChapter)
                    (ChGrid.Children[bookmarks[m.id].lastChapter] as Button).Background = null;
                bookmarks[m.id].lastChapter = chIndex;
                bookmarks[m.id].lastRead = DateTime.UtcNow;
            }
            else
                bookmarks.Add(m.id, new MangaBookmark() { id = m.id, lastChapter = chIndex, lastRead = DateTime.UtcNow });

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

            //Update bookmark buttons
            if (ChGrid.Children.Count > chIndex)
                if (curManga == null)
                    (ChGrid.Children[chIndex] as Button).Background = Brushes.DarkGoldenrod;
                else if (curManga == m) {
                    (ChGrid.Children[curChIndex] as Button).Background = null;
                    (ChGrid.Children[chIndex] as Button).Background = Brushes.DarkGoldenrod;
                }
            curManga = m;
            curChIndex = chIndex;

            //Update top bar
            BarChName.Text = m.title + ", " + api.GetChapterName(m, chIndex);
            hasPrev = chIndex > 0;
            hasNext = chIndex < m.chapters.Length - 1;

            if (LeftToRight) {//Makes sure the left and right butts are properly switched when using different read directions
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

            if (hasNext)//Prefetch next chapter if applicable
                AsyncPrefetchChapter(m, chIndex + 1);
            GC.Collect();
        }

        public string ProgressTip {
            set {
                BarProgessText.Text = ProgessText.Text = value;
            }
        }

        public void ToggleSearchPane(object sender, RoutedEventArgs e) {
            SearchPane.Visibility = SearchPane.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
        }

        //Left and right butt does different things when slapped based on which direction the images are laid out in
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

        public void ShowSearchPane(object sender, KeyEventArgs args) {
            if (args.Key == Key.S)
                SearchPane.Visibility = Visibility.Visible;
        }

        //Loads the next chapter from prefetched cache.
        public void LoadNext() {
            if (prefetchedData == null)
                loadPrefetchOnComplete = true;//Load on finish if prefetching is not done
            else
                LoadChapterFromData(prefetchedData, curManga, curChIndex + 1);
        }

        public void LoadPrev() {
            AsyncFetchChapter(curManga, curChIndex - 1);
        }

        public bool LeftToRight {
            set {//Changes layout direciton and update the left and right buttons
                ImagePane.FlowDirection = value ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
                DisplayScroll.ScrollToHorizontalOffset(DisplayScroll.ScrollableWidth - DisplayScroll.HorizontalOffset);
                if (value) {
                    LeftButt.Visibility = hasPrev ? Visibility.Visible : Visibility.Hidden;
                    RightButt.Visibility = hasNext ? Visibility.Visible : Visibility.Hidden;
                    DisplayScroll.InvertMouseWheel = false;
                    LeftButt.ToolTip = "Prev";
                    RightButt.ToolTip = prefetchedData == null ? "Next (Fetching...)" : "Next";
                }
                else {
                    LeftButt.Visibility = hasNext ? Visibility.Visible : Visibility.Hidden;
                    RightButt.Visibility = hasPrev ? Visibility.Visible : Visibility.Hidden;
                    DisplayScroll.InvertMouseWheel = true;
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
                    DisplayScroll.Effect = ZoomImg.Effect = new InvertEffect();
                else
                    DisplayScroll.Effect = ZoomImg.Effect = null;
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
                        bookmarks.Select(x => {
                            if (x.Key == "") return null;
                            return new XElement("bookmarks",
                                    new XAttribute("k", x.Key),
                                    new XAttribute("v", x.Value.lastChapter),
                                    new XAttribute("d", x.Value.lastRead.ToString()));
                        })
                     );
                root.Add(xElem);
                doc.Add(root);
                doc.Save(USER_DATA_FILE);
            }).Start();
        }

        #endregion
    }
}
