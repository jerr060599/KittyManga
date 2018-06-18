﻿//Copyright (c) 2018 Chi Cheng Hsu
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
            foreach (MangaAddress a in mainIndex.manga) {
                a.t = System.Net.WebUtility.HtmlDecode(a.t);
                a.a = System.Net.WebUtility.HtmlDecode(a.a);
                float s = (float)Math.Log(a.h + 1);
                minHit = Math.Min(minHit, s);
                maxHit = Math.Max(maxHit, s);
            }
            foreach (MangaAddress a in mainIndex.manga)
                a.popWeight = (float)((Math.Log(a.h + 1) - minHit) / (maxHit - minHit));
        }

        /// <summary>
        /// Fetches the cover of a manga synchronously.
        /// </summary>
        /// <param name="m">The manga</param>
        /// <returns>The downloaded image</returns>
        public BitmapImage FetchCover(Manga m) {
            return DownloadImage(API_IMG + (string)m.image);
        }

        /// <summary>
        /// Downloads a single image from url synchronously.
        /// Use DownloadImages for multiple images
        /// </summary>
        /// <param name="url">The url</param>
        /// <returns>The downloaded image</returns>
        public BitmapImage DownloadImage(string url) {
            using (WebClient client = new WebClient()) {
                return LoadImage(client.DownloadData(url));
            }
        }

        /// <summary>
        /// A struct that contains info for downnloading multiple images with Download Images
        /// </summary>
        public struct DownloadOrder {
            /// <summary>
            /// The url to download from
            /// </summary>
            public string url;
            /// <summary>
            /// A id that specifies the index of the image in the return array
            /// </summary>
            public int id;
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

                Queue<DownloadOrder> q = new Queue<DownloadOrder>();
                for (int i = 0; i < c.images.Length; i++)
                    q.Enqueue(new DownloadOrder() {
                        url = API_IMG + (string)c.images[i][1],
                        id = c.images.Length - i - 1
                    });

                return DownloadImages(q, sender, numClient);
            }
        }

        /// <summary>
        /// Fetches multiple images simultaneously
        /// </summary>
        /// <param name="q">A queue of DownloadOrders that contain where to download and where to put it</param>
        /// <param name="sender">A background worker to report progress to</param>
        /// <param name="numClient">The number of simultaneous connections to make</param>
        /// <returns>The downloaded images</returns>
        public BitmapImage[] DownloadImages(Queue<DownloadOrder> q, BackgroundWorker sender = null, int numThreads = 5) {
            BitmapImage[] data = new BitmapImage[q.Count];
            Thread[] threads = new Thread[numThreads];
            int all = q.Count;
            int done = 0;
            for (int i = 0; i < numThreads; i++) {
                threads[i] = new Thread(() => {
                    using (WebClient tc = new WebClient()) {
                        while (true) {
                            DownloadOrder order;
                            lock (q) {
                                if (q.Count == 0)
                                    break;
                                order = q.Dequeue();
                            }
                            data[order.id] = LoadImage(tc.DownloadData(order.url));
                            done++;
                            if (sender != null)
                                sender.ReportProgress(all - done);
                        }
                    }
                });
                threads[i].Start();
            }
            for (int i = 0; i < numThreads; i++) threads[i].Join();
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
            using (WebClient client = new WebClient()) {
                Manga m = JsonConvert.DeserializeObject<Manga>(client.DownloadString(API_BASE + $@"/manga/{mainIndex.manga[index].i}/"));
                m.title = System.Net.WebUtility.HtmlDecode(m.title);
                m.alias = System.Net.WebUtility.HtmlDecode(m.alias);
                m.description = System.Net.WebUtility.HtmlDecode(m.description);
                Array.Reverse(m.chapters);
                for (int i = 0; i < m.chapters.Length; i++)
                    if (m.chapters[i][0] != null)
                        m.chapters[i][0] = float.Parse(m.chapters[i][0].ToString());
                m.id = mainIndex.manga[index].i;
                return m;
            }
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
                return Math.Sign(other.s - s);
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
            List<MangaMatch> heap = new List<MangaMatch>();
            for (int i = 0; i < mainIndex.manga.Length; i++) {
                MangaAddress a = mainIndex.manga[i];
                float score = 1f - Math.Min(EditDist(a.t.ToLower(), term) / (float)(term.Length + a.t.Length),
                                            EditDist(a.a.ToLower(), term) / (float)(term.Length + a.a.Length));
                score = (float)Math.Pow(score, 1 / (1 + a.popWeight));
                if (heap.Count < top) {
                    heap.Add(new MangaMatch() { s = score, index = i });
                    heap.Sort();
                }
                else if (heap.Last().s < score) {
                    heap[heap.Count - 1].s = score;
                    heap[heap.Count - 1].index = i;
                    heap.Sort();
                }
            }
            return heap;
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

    public class MangaAddress {
        public string a;
        public string[] c;
        public int h;
        public string i;
        public string im;
        public float ld;
        public int s;
        public string t;
        public float popWeight;
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
        public int hits;
        public object image;
        public string last_chapter_date;
        public object released;
        public string id;
    }

    public class Chapter {
        public object[][] images;
    }
}
