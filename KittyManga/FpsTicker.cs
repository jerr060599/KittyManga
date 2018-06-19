//Copyright (c) 2018 Chi Cheng Hsu
//MIT License

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace KittyManga {
    /// <summary>
    /// A clock similar to DispatcherTimer. 
    /// It invokes the update function at a set fps with the deltaTime since last update.
    /// The clock will run at the set fps unless the update method take too long to maintain the frequency.
    /// </summary>
    class FpsTicker {
        /// <summary>
        /// The target FPS (frames per second) of the internal clock. 
        /// </summary>
        public double FPS = 60;
        /// <summary>
        /// The Action invoked on a tick. The parameter is a double repersenting the delta time since last update.
        /// </summary>
        public Action<double> Tick;

        Action<ulong> InvokeUpdate;
        double LastUpdateDuration = 0;
        ulong lastUpdateId = 0;
        Thread thread;
        double LastInvoke;
        System.Diagnostics.Stopwatch watch;

        public FpsTicker() {
            InvokeUpdate = OnInvokeUpdate;
            watch = new System.Diagnostics.Stopwatch();
            watch.Start();
        }

        ~FpsTicker() {
            watch.Stop();
            watch = null;
        }

        /// <summary>
        /// Starts the clock of this FpsTicker.
        /// </summary>
        public void Start() {
            //This thread is primarily concerned with generating the proper frequency unless it can't be maintained. The rest of the logic is handled in InvokeUpdate
            thread = new Thread(() => {
                try {
                    LastInvoke = Now;
                    double nextUpdate = Now;
                    ulong updateID = 0;
                    while (true) {
                        nextUpdate += Math.Max(1 / FPS, LastUpdateDuration);
                        if (Application.Current == null)
                            Thread.CurrentThread.Abort();
                        if (Tick != null) try {
                                updateID++;
                                Application.Current.Dispatcher.Invoke(InvokeUpdate, System.Windows.Threading.DispatcherPriority.Normal, updateID);
                            }
                            catch (TaskCanceledException) { }
                        while (true) {
                            double dt = Now - nextUpdate;
                            if (Math.Abs(dt) > 100)
                                nextUpdate = Now;//Fix cases where stopwatch may "jump" i.e. if system goes to sleep and wakes up
                            else if (dt > 0)
                                break;
                            Thread.Sleep(1); Thread.Yield();
                            Thread.Sleep(1); Thread.Yield();
                        };
                    }
                }
                catch (ThreadAbortException) {
                    Thread.ResetAbort();
                }
            });
            thread.Start();
        }

        /// <summary>
        /// Stops the clock of this FpsTicker
        /// </summary>
        public void Stop() {
            thread.Abort();
            thread = null;
        }

        /// <summary>
        /// Gets the current time in seconds since the ticker have been created
        /// </summary>
        public double Now {
            get { return watch.Elapsed.TotalSeconds; }
        }

        /// <summary>
        /// Because the time it takes for Application.Current.Dispatcher to call this can be inconsistent, delta time logic is handled here.
        /// </summary>
        /// <param name="id">The strictly increasing id of the update. </param>
        void OnInvokeUpdate(ulong id) {
            if (id < lastUpdateId)
                return;
            //Im fairly sure Dispatcher calls things in order but this is to be 100% sure. 
            //This will effectively merge older updates if a newer update call comes through
            lastUpdateId = id;

            //Calculates delta time
            double start = Now;
            double delta = start - LastInvoke;
            LastInvoke = start;


            Tick.Invoke(delta);

            //Takes note of how long the Update took to adjust the clock
            LastUpdateDuration = Now - start;
        }
    }
}
