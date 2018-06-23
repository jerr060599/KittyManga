//Copyright (c) 2018 Chi Cheng Hsu
//MIT License

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using System.Windows.Media.Imaging;
using System.Threading;
using System.ComponentModel;

namespace KittyManga {
    class MangaAPI {
        public const string API_BASE = @"https://www.mangaeden.com/api";
        public const string API_IMG = @"https://cdn.mangaeden.com/mangasimg/";
        /// <summary>
        /// THe name of the local file to search for when loading main index
        /// </summary>
        public string INDEX_FILE = @"MangaIndex.txt";
        public string COVERS_CACHE_DIR = @"\Covers\";

        public MangaAddress this[int i] {
            get { return mainIndex.manga[i]; }
        }

        /// <summary>
        /// The main index containing overview info for every manga availible
        /// </summary>
        public AddressInfo mainIndex = null;

        /// <summary>
        /// Fetchs the main index of mangas from MangaEden or local file synchronously.
        /// </summary>
        /// <param name="tryLoadFile">Whether it will try to load from file before contacting MangaEden</param>
        public void FetchIndex(bool tryLoadFile = true) {
            if (tryLoadFile && File.Exists(INDEX_FILE)) {
                try {
                    using (StreamReader r = new StreamReader(File.OpenRead(INDEX_FILE))) {
                        DateTime date = DateTime.Parse(r.ReadLine());

                        if (DateTime.Now.Day != date.Day) {
                            r.Close();
                            FetchIndex(false);
                        }
                        else {
                            mainIndex = JsonConvert.DeserializeObject<AddressInfo>(r.ReadToEnd());
                            r.Close();
                            ProcessIndex();
                        }
                    }
                }
                catch (Exception) {
                    FetchIndex(false);
                }
            }
            else {
                using (WebClient client = new WebClient()) {
                    //Download from internet
                    string data = client.DownloadString(API_BASE + @"/list/0/");
                    mainIndex = JsonConvert.DeserializeObject<AddressInfo>(data);
                    ProcessIndex();

                    try {//Try to save it
                        StreamWriter w = new StreamWriter(File.Open(INDEX_FILE, FileMode.Create));
                        w.WriteLine(DateTime.Now.ToString());
                        w.Write(data);
                        w.Close();
                    }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// Processes the index after load to clean up html charectors and calculate popularity weight for search function
        /// </summary>
        private void ProcessIndex() {
            float minHit = float.MaxValue, maxHit = float.MinValue;
            for (int i = 0; i < mainIndex.manga.Length; i++) {
                MangaAddress a = mainIndex.manga[i];
                a.t = System.Net.WebUtility.HtmlDecode(a.t);
                a.a = System.Net.WebUtility.HtmlDecode(a.a);
                float s = (float)Math.Log(a.h + 1);
                minHit = Math.Min(minHit, s);
                maxHit = Math.Max(maxHit, s);
                a.idHash = a.i.GetHashCode();
                mainIndex.manga[i] = a;
            }
            for (int i = 0; i < mainIndex.manga.Length; i++)
                mainIndex.manga[i].popWeight = (float)((Math.Log(mainIndex.manga[i].h + 1) - minHit) / (maxHit - minHit));
        }

        /// <summary>
        /// Fetches the cover of a manga synchronously.
        /// </summary>
        /// <param name="m">The manga</param>
        /// <returns>The downloaded image</returns>
        public BitmapImage FetchCover(Manga m) {
            if (m.image == null) return null;
            if (m.isLocal)
                if (m.chapters.Length > 0) {
                    foreach (var item in Directory.GetFiles((string)m.chapters[0][3])) {
                        string p = item.ToLower();
                        if (p.EndsWith(".png") || p.EndsWith(".jpg") || p.EndsWith(".jpeg")) {
                            var img = new BitmapImage(new Uri(item));
                            img.Freeze();
                            return img;
                        }
                    }
                    return null;
                }
                else return null;
            else
                return DownloadImage(API_IMG + (string)m.image);
        }

        /// <summary>
        /// Fetches the cover of a manga synchronously.
        /// </summary>
        /// <param name="i">The indxe of the manga</param>
        /// <returns>The downloaded image</returns>
        public BitmapImage FetchCover(int i) {
            if (mainIndex.manga[i].im == null) return null;
            BitmapImage img;
            string cachePath = AppDomain.CurrentDomain.BaseDirectory + COVERS_CACHE_DIR + (string)mainIndex.manga[i].im;
            if (File.Exists(cachePath)) {
                img = new BitmapImage(new Uri(cachePath));
            }
            else {
                img = DownloadImage(API_IMG + (string)mainIndex.manga[i].im);
                if (img == null) return null;
                SaveImage(img, cachePath);
            }
            img.Freeze();
            return img;
        }

        /// <summary>
        /// Saves an image
        /// </summary>
        /// <param name="img"></param>
        /// <param name="path"></param>
        public void SaveImage(BitmapImage img, string path) {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(img));
            if (!Directory.Exists(Path.GetDirectoryName(path)))
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var fileStream = new FileStream(path, FileMode.Create)) {
                encoder.Save(fileStream);
            }
        }

        /// <summary>
        /// Downloads a single image from url synchronously.
        /// Use DownloadImages for multiple images
        /// </summary>
        /// <param name="url">The url</param>
        /// <returns>The downloaded image</returns>
        public BitmapImage DownloadImage(string url) {
            using (WebClient client = new WebClient()) {
                try {
                    return LoadImage(client.DownloadData(url));
                }
                catch (WebException) { return null; }
            }
        }

        /// <summary>
        /// Fetches a chapter from mangaeden synchronously.
        /// </summary>
        /// <param name="chapterId">The string chapter id</param>
        /// <param name="sender">A background worker to report progress to</param>
        /// <param name="numClient">The number of simultaneous connections to make</param>
        /// <returns>The downloaded images</returns>
        public BitmapImage[] FetchChapter(string chapterId, BackgroundWorker sender = null, int numClient = 8) {
            using (WebClient client = new WebClient()) {
                Chapter c = JsonConvert.DeserializeObject<Chapter>(client.DownloadString(API_BASE + $@"/chapter/{chapterId}/"));

                string[] urls = new string[c.images.Length];
                for (int i = 0; i < c.images.Length; i++)
                    urls[c.images.Length - i - 1] = API_IMG + (string)c.images[i][1];

                return DownloadImages(urls, sender, numClient);
            }
        }

        /// <summary>
        /// Load a chapter from a local path
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns>The loaded images</returns>
        public BitmapImage[] LoadChapter(Manga m, int index) {
            List<BitmapImage> imgs = new List<BitmapImage>();
            string[] files = Directory.GetFiles((string)m.chapters[index][3]);
            Array.Sort(files);
            foreach (var item in files) {
                string p = item.ToLower();
                if (p.EndsWith(".png") || p.EndsWith(".jpg") || p.EndsWith(".jpeg")) {
                    BitmapImage img = new BitmapImage(new Uri(p));
                    img.Freeze();
                    imgs.Add(img);
                }
            }
            return imgs.ToArray();
        }

        /// <summary>
        /// Fetches multiple images through multiple connections simultaneously
        /// </summary>
        /// <param name="urls">A list of urls to download from</param>
        /// <param name="sender">A background worker to report progress to</param>
        /// <param name="numClient">The number of simultaneous connections to make</param>
        /// <returns>The downloaded images</returns>
        public BitmapImage[] DownloadImages(string[] urls, BackgroundWorker sender = null, int numThreads = 5) {
            BitmapImage[] data = new BitmapImage[urls.Length];
            Thread[] threads = new Thread[numThreads];
            Thread cur = Thread.CurrentThread;
            int done = 0;
            int next = 0;
            for (int i = 0; i < numThreads; i++) {
                threads[i] = new Thread(() => {
                    using (WebClient tc = new WebClient()) {
                        while (true) {
                            int index;
                            lock (urls) {
                                if (next >= urls.Length)
                                    break;
                                index = next;
                                next++;
                            }
                            if (!cur.IsAlive) break;
                            try {
                                data[index] = LoadImage(tc.DownloadData(urls[index]));
                            }
                            catch (WebException) { }
                            done++;
                            if (!cur.IsAlive) break;
                            else if (sender != null && sender.IsBusy)
                                sender.ReportProgress(urls.Length - done);
                        }
                    }
                });
                threads[i].Start();
            }
            foreach (Thread t in threads) t.Join();
            return data;
        }

        /// <summary>
        /// Loads an image from a byte[]
        /// </summary>
        /// <param name="imageData">The data</param>
        /// <returns>The image</returns>
        public BitmapImage LoadImage(byte[] imageData) {
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (MemoryStream mem = new MemoryStream(imageData)) {
                mem.Position = 0;
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Gets the chapter name from a manga. 
        /// This is used because chapter names are inconsistent in MangaEdens api.
        /// Some chapters use chapter numbers, some use titles, some use chapter numbers in the title field.
        /// This formats everything to be consistent.
        /// </summary>
        /// <param name="m">The manga</param>
        /// <param name="i">The index of the chapter</param>
        /// <returns>The formated name</returns>
        public string GetChapterName(Manga m, int i) {
            if (m.chapters[i][0] != null)
                if (m.chapters[i][2] != null)
                    if ((string)m.chapters[i][2] == ((float)m.chapters[i][0]).ToString())
                        return $"Ch.{(string)m.chapters[i][2]}";
                    else
                        return $"Ch.{(float)m.chapters[i][0]}: {(string)m.chapters[i][2]} ";
                else
                    return $"Ch.{(float)m.chapters[i][0]}";
            else if (m.chapters[i][2] != null)
                return $"{(string)m.chapters[i][2]}";
            else
                return $"?Ch.{i + 1}?";
        }

        /// <summary>
        /// Fetches more detailed manga info synchronously.
        /// </summary>
        /// <param name="index">The index in the mainIndex</param>
        /// <returns>The manga</returns>
        public Manga FetchManga(int index) {
            return FetchManga(mainIndex.manga[index].i);
        }

        /// <summary>
        /// Fetches more detailed manga info synchronously.
        /// </summary>
        /// <param name="id">id</param>
        /// <returns>The manga</returns>
        public Manga FetchManga(string id) {
            using (WebClient client = new WebClient()) {
                Manga m = JsonConvert.DeserializeObject<Manga>(client.DownloadString(API_BASE + $@"/manga/{id}/"));
                m.title = System.Net.WebUtility.HtmlDecode(m.title);
                m.alias = System.Net.WebUtility.HtmlDecode(m.alias);
                m.description = System.Net.WebUtility.HtmlDecode(m.description);
                Array.Reverse(m.chapters);
                for (int i = 0; i < m.chapters.Length; i++)
                    if (m.chapters[i][0] != null)
                        m.chapters[i][0] = float.Parse(m.chapters[i][0].ToString());
                m.id = id;
                return m;
            }
        }

        /// <summary>
        /// Loads a manga from a path and its subfolders
        /// Chapters are laid out based on discovery order the bfs search and their names
        /// Chapters discovered first will always come first
        /// If the discoervy depth is the same, then they are sorted by name
        /// </summary>
        /// <param name="path">The local path on disk</param>
        /// <returns></returns>
        public Manga LoadManga(string path) {
            Manga m = new Manga();
            m.title = Path.GetFileName(path);
            m.author = "Meow!";
            m.categories = new string[] { "Local" };
            m.description = "Local files: \n" + path + "\n";
            m.released = DateTime.Now.Year;
            m.isLocal = true;

            Queue<string> q = new Queue<string>();
            Heap<string> heap = new Heap<string>(string.Compare);
            List<object[]> chapters = new List<object[]>();
            q.Enqueue(path);
            double time = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

            //Move through file system to find folders with atleast one image file and sort them according to the rule
            while (q.Count > 0) {
                string n = q.Dequeue();
                //Checks for image file
                foreach (var item in Directory.GetFiles(n)) {
                    string p = item.ToLower();
                    if (p.EndsWith(".png") || p.EndsWith(".jpg") || p.EndsWith(".jpeg")) {
                        chapters.Add(new object[] { null, time, Path.GetFileName(n), n });
                        break;
                    }
                }
                string[] paths = Directory.GetDirectories(n);
                Array.Sort(paths);
                foreach (var item in paths)
                    q.Enqueue(item);
            }
            m.chapters = chapters.ToArray();

            return m;
        }

        /// <summary>
        /// A class containing the top matches in a search
        /// </summary>
        public class MangaMatch : IComparable<MangaMatch> {
            /// <summary>
            /// The score from 0 to 1. 1 if perfect match. 0 if no match
            /// </summary>
            public float s;
            /// <summary>
            /// The manga index in mainIndex
            /// </summary>
            public int index;

            public int CompareTo(MangaMatch other) {
                return Math.Sign(s - other.s);
            }
        }

        /// <summary>
        /// Searches the mainIndex for a manga
        /// </summary>
        /// <param name="term">Search term</param>
        /// <param name="top">The number of results to return</param>
        /// <returns>Results</returns>
        public List<MangaMatch> SearchIndex(string term, int top = 10) {
            if (mainIndex == null)
                FetchIndex();
            term = term.ToLower();
            Heap<MangaMatch> heap = new Heap<MangaMatch>((a, b) => a.CompareTo(b));
            for (int i = 0; i < mainIndex.manga.Length; i++) {
                MangaAddress a = mainIndex.manga[i];
                float score = 1f - Math.Min(EditDist(a.t.ToLower(), term) / (float)(term.Length + a.t.Length),
                                            EditDist(a.a.ToLower(), term) / (float)(term.Length + a.a.Length));
                score = (float)Math.Pow(score, 1 / (1 + a.popWeight));
                if (heap.Count < top) {
                    heap.Add(new MangaMatch() { s = score, index = i });
                }
                else if (heap.Min.s < score) {
                    heap.RemoveMin();
                    heap.Add(new MangaMatch() { s = score, index = i });
                }
            }
            return heap.ToList();
        }

        /// <summary>
        /// Fetches the most recently update manga
        /// </summary>
        /// <param name="top">The number to return</param>
        /// <returns>The most recent mangas</returns>
        public int[] FetchUpdated(int top = 10) {
            var heap = new Heap<KeyValuePair<int, double>>((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < mainIndex.manga.Length; i++) {
                if (mainIndex.manga[i].ld == null)
                    continue;
                if (heap.Count < top)
                    heap.Add(new KeyValuePair<int, double>(i, (double)mainIndex.manga[i].ld));
                else if (heap.Min.Value < (double)mainIndex.manga[i].ld) {
                    heap.RemoveMin();
                    heap.Add(new KeyValuePair<int, double>(i, (double)mainIndex.manga[i].ld));
                }
            }
            return heap.Select<KeyValuePair<int, double>, int>(x => x.Key).ToArray();
        }

        int[,] dpMat = new int[400, 400];
        /// <summary>
        /// The string edit distance with transposition
        /// </summary>
        /// <param name="original">Original string</param>
        /// <param name="modified">Modyfied string</param>
        /// <returns>Edit distance</returns>
        int EditDist(string original, string modified) {
            int len_orig = original.Length;
            int len_diff = modified.Length;
            if (len_orig >= dpMat.GetLength(0) || len_diff >= dpMat.GetLength(1))
                return len_orig + len_diff;
            lock (this) {
                for (int i = 1; i <= len_orig; i++)
                    for (int j = 1; j <= len_diff; j++)
                        dpMat[i, j] = 0;
                for (int i = 0; i <= len_orig; i++)
                    dpMat[i, 0] = i;
                for (int j = 0; j <= len_diff; j++)
                    dpMat[0, j] = j;

                for (int i = 1; i <= len_orig; i++)
                    for (int j = 1; j <= len_diff; j++) {
                        int cost = modified[j - 1] == original[i - 1] ? 0 : 1;
                        dpMat[i, j] = Math.Min(dpMat[i - 1, j] + 1, Math.Min(dpMat[i, j - 1] + 1, dpMat[i - 1, j - 1] + cost));
                        if (i > 1 && j > 1 && original[i - 1] == modified[j - 2] && original[i - 2] == modified[j - 1])
                            dpMat[i, j] = Math.Min(dpMat[i, j], dpMat[i - 2, j - 2] + cost);
                    }
                return dpMat[len_orig, len_diff];
            }
        }
    }

    public class AddressInfo {
        public MangaAddress[] manga;
        public int end;
        public int page;
        public int start;
        public int total;
    }

    public struct MangaAddress {
        public string a;
        public string[] c;
        public int h;
        public string i;
        public string im;
        public object ld;
        public int s;
        public string t;
        public float popWeight;
        public int idHash;
    }

    public class Manga {
        public string title;
        public string[] aka;
        public string alias;
        public string artist;
        public string author;
        public string[] categories;
        public object[][] chapters;
        public double created;
        public string description;
        public int hits = 1;
        public object image;
        public string last_chapter_date;
        public object released;
        public string id;
        public bool isLocal = false;
    }

    public class Chapter {
        public object[][] images;
    }
}
