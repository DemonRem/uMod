﻿using System.Threading;

namespace uMod.Logging
{
    /// <summary>
    /// Represents a logger that processes messages on a worker thread
    /// </summary>
    public abstract class ThreadedLogger : Logger
    {
        // Sync mechanisms
        private AutoResetEvent waitevent;
        private bool exit;
        private object syncroot;

        // The worker thread
        private Thread workerthread;

        /// <summary>
        /// Initializes a new instance of the ThreadedLogger class
        /// </summary>
        public ThreadedLogger() : base(false)
        {
            // Initialize
            waitevent = new AutoResetEvent(false);
            exit = false;
            syncroot = new object();

            // Create the thread
            workerthread = new Thread(Worker) { IsBackground = true };
            workerthread.Start();
        }

        ~ThreadedLogger()
        {
            OnRemoved();
        }

        public override void OnRemoved()
        {
            if (exit)
            {
                return;
            }

            exit = true;
            waitevent.Set();
            workerthread.Join();
        }

        /// <summary>
        /// Writes a message to the current logfile
        /// </summary>
        /// <param name="msg"></param>
        internal override void Write(LogMessage msg)
        {
            lock (syncroot)
            {
                base.Write(msg);
            }

            waitevent.Set();
        }

        /// <summary>
        /// Begins a batch process operation
        /// </summary>
        protected abstract void BeginBatchProcess();

        /// <summary>
        /// Finishes a batch process operation
        /// </summary>
        protected abstract void FinishBatchProcess();

        /// <summary>
        /// The worker thread
        /// </summary>
        private void Worker()
        {
            // Loop until it's time to exit
            while (!exit)
            {
                // Wait for signal
                waitevent.WaitOne();

                // Iterate each item in the queue
                lock (syncroot)
                {
                    if (MessageQueue.Count <= 0)
                    {
                        continue;
                    }

                    BeginBatchProcess();
                    try
                    {
                        while (MessageQueue.Count > 0)
                        {
                            // Dequeue
                            LogMessage message = MessageQueue.Dequeue();

                            // Process
                            ProcessMessage(message);
                        }
                    }
                    finally
                    {
                        FinishBatchProcess();
                    }
                }
            }
        }
    }
}
