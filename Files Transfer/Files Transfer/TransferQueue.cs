using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Files_Transfer
{
    public enum QueueType : byte
    {
        download,
        upload
    }
    public class TransferQueue
    {
        //to use the private constructor of this class
        public static TransferQueue createUploadQueue(TransferClient transferClient, string fileName)
        {
            try
            {
                var queue = new TransferQueue();
                queue.fileName = Path.GetFileName(fileName);
                queue.client = transferClient;
                queue.type = QueueType.upload;
                queue.FS = new FileStream(fileName, FileMode.Open);
                queue.thread = new Thread(new ParameterizedThreadStart(transferPro));
                queue.thread.IsBackground = true;
                queue.ID = Program.rand.Next();
                queue.length = queue.FS.Length;
                return queue;
            }
            catch
            {
                return null;
            }
        }

        public static TransferQueue createDowloadQueue(TransferClient transferClient, int id, String saveName, long length)
        {
            try
            {
                var queue = new TransferQueue();
                queue.fileName = Path.GetFileName(saveName);
                queue.client = transferClient;
                queue.ID = id;
                queue.type = QueueType.download;
                queue.FS = new FileStream(saveName, FileMode.Create);
                queue.FS.SetLength(length);
                queue.length = length;
                return queue;
            }
            catch
            {
                return null;
            }
        }
        //FILE_BUFFER_SIZE : size of file send will be 8klb or under.
        private const int FILE_BUFFER_SIZE = 8175;
        private static byte[] file_buffer = new byte[FILE_BUFFER_SIZE];

        //use to pause our upload or download.
        private ManualResetEvent pauseEvent;

        public int ID;
        public int progress, lastProgress;

        public long transferred;
        public long index;
        public long length;

        public bool running;
        public bool paused;

        public string fileName;

        public QueueType type;

        public TransferClient client;
        public Thread thread;   //it's used to thread for upload
        public FileStream FS; //read file upload and download.

        //Constructor.
        private TransferQueue()
        {
            pauseEvent = new ManualResetEvent(true);
            running = true;
        }

        public void start()
        {
            running = true;
            thread.Start();
        }

        public void stop()
        {
            running = false;
        }

        public void pause()
        {
            if (!paused)
            {
                pauseEvent.Reset();
            }
            else
            {
                pauseEvent.Set();
            }
            paused = !paused;
        }

        public void close()
        {
            try
            {
                client.transfers.Remove(ID);
            }
            catch
            {
            }
            running = false;
            FS.Close();
            pauseEvent.Dispose();

            client = null;
        }

        public void write(byte[] bytes, long index)
        {
            lock (this)
            {
                FS.Position = index;
                FS.Write(bytes, 0, bytes.Length);
                transferred += bytes.Length;
            }
        }

        public static void transferPro(object o)
        {
            TransferQueue queue = (TransferQueue)o;
            while (queue.running && queue.index < queue.length)
            {
                queue.pauseEvent.WaitOne();
                if (!queue.running)
                {
                    break;
                }
                lock (file_buffer)
                {
                    queue.FS.Position = queue.index;
                    int read = queue.FS.Read(file_buffer, 0, file_buffer.Length);

                    PacketWriter pw = new PacketWriter();
                    pw.Write((byte)header.chunk);
                    pw.Write(queue.ID);
                    pw.Write(queue.index);
                    pw.Write(read);
                    pw.Write(file_buffer, 0, read);

                    queue.transferred += read;
                    queue.index += read;
                    queue.client.send(pw.GetBytes());
                    queue.progress = (int)((queue.transferred * 100)/queue.length);

                    if (queue.lastProgress < queue.progress)
                    {
                        queue.lastProgress = queue.progress;
                        queue.client.callProgressChanged(queue);
                    }
                    Thread.Sleep(1);
                }
            }
            queue.close();
        }

    }
}
