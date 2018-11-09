using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Files_Transfer
{
    public delegate void TransferEventHandler(object sender, TransferQueue queue);
    public delegate void ConnectCallBack(object sender, string error);

    public class TransferClient
    {
        private Socket _baseSocket;
        private byte[] _buffer = new byte[8192];
        private ConnectCallBack _connectCallBack;

        private Dictionary<int, TransferQueue> _transfers = new Dictionary<int, TransferQueue>();

        public Dictionary<int, TransferQueue> transfers {
            get { return _transfers; }
        }
        
        public bool closed {
            get; private set;
        }

        public string outputFolder {
            get; set;
        }

        public IPEndPoint endPoint {
            get; private set;
        }

        public event TransferEventHandler queued;
        public event TransferEventHandler progressChanged;
        public event TransferEventHandler stopped;
        public event TransferEventHandler complete;
        public event EventHandler disconnected;

        //constructor;
        public TransferClient()
        {
            _baseSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public TransferClient(Socket sock)
        {
            _baseSocket = sock;
            endPoint = (IPEndPoint)_baseSocket.RemoteEndPoint;
        }

        public void connect(string hostName, int port, ConnectCallBack callBack)
        {
            _connectCallBack = callBack;
            _baseSocket.BeginConnect(hostName, port, connectCallBack, null);
        }

        private void connectCallBack(IAsyncResult ias)
        {
            string error = null;
            try
            {
                _baseSocket.EndConnect(ias);
                endPoint = (IPEndPoint)_baseSocket.RemoteEndPoint;
            }
            catch(Exception ex)
            {
                error = ex.Message;
            }
            _connectCallBack(this, error);
        }

        public void run()
        {
            try
            {
                _baseSocket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.Peek, receiveCallBack, null);
            }
            catch
            {
            }
        }

        public void queueTransfer(string fileName)
        {
            try
            {
                TransferQueue queue = TransferQueue.createUploadQueue(this, fileName);
                _transfers.Add(queue.ID, queue);
                PacketWriter pw = new PacketWriter();
                pw.Write((byte)header.queue);
                pw.Write(queue.ID);
                pw.Write(queue.length);
                pw.Write(queue.fileName);
                send(pw.GetBytes());
                if (queued != null)
                {
                    queued(this, queue);
                }
            }
            catch
            {

            }
        }

        public void startTransfer(TransferQueue queue)
        {
            PacketWriter pw = new PacketWriter();
            pw.Write((byte)header.start);
            pw.Write(queue.ID);
            send(pw.GetBytes());
        }

        public void stopTransfer(TransferQueue queue)
        {
            if (queue.type == QueueType.upload)
            {
                queue.stop();
            }
            PacketWriter pw = new PacketWriter();
            pw.Write((byte)header.stop);
            pw.Write(queue.ID);
            send(pw.GetBytes());
        }

        public void pauseTransfer(TransferQueue queue)
        {
            if (queue.type == QueueType.upload)
            {
                queue.pause();
                return;
            }
            PacketWriter pw = new PacketWriter();
            pw.Write((byte)header.stop);
            pw.Write(queue.ID);
            send(pw.GetBytes());
        }

        public int getOverallProgress()
        {
            int overall = 0;
            foreach (KeyValuePair<int, TransferQueue> pair in _transfers)
            {
                overall += pair.Value.progress;
            }
            if(overall > 0)
            {
                overall = (overall * 100) / (_transfers.Count * 100);
            }
            return overall;
        }

        public void send(byte[] data)
        {
            if (closed)
            {
                return;
            }
            lock (this)
            {
                try
                {
                    _baseSocket.Send(BitConverter.GetBytes(data.Length), 0, 4, SocketFlags.None);
                    _baseSocket.Send(data, 0, data.Length, SocketFlags.None);
                }
                catch
                {
                    close();
                }
            }
        }

        public void close()
        {
            closed = true;
            _baseSocket.Close();
            _transfers.Clear();
            _transfers = null;
            _buffer = null;
            outputFolder = null;

            if (disconnected != null)
                disconnected(this, EventArgs.Empty);
        }

        public void process()
        {
            PacketReader pr = new PacketReader(_buffer);
            header head = (header)pr.ReadByte();

            switch (head)
            {
                case header.queue:
                    {
                        int id = pr.ReadInt32();
                        string fileName = pr.ReadString();
                        long length = pr.ReadInt64();

                        TransferQueue transferQueue = TransferQueue.createDowloadQueue(this, id, 
                            Path.Combine(outputFolder,Path.GetFileName(fileName)), length);
                        _transfers.Add(id, transferQueue);
                    }
                    break;
                case header.start:
                    {
                        int id = pr.ReadInt32();
                        if (_transfers.ContainsKey(id))
                        {
                            _transfers[id].start();
                        }
                    }
                    break;
                case header.stop:
                    {
                        int id = pr.ReadInt32();
                        if (_transfers.ContainsKey(id))
                        {
                            TransferQueue transferQueue = _transfers[id];
                            transferQueue.stop();
                            transferQueue.close();
                            if(null != stopped)
                            {
                                stopped(this, transferQueue);
                            }
                            _transfers.Remove(id);
                        }
                    }
                    break;
                case header.pause:
                    {
                        int id = pr.ReadInt32();
                        if (_transfers.ContainsKey(id))
                        {
                            _transfers[id].pause();
                        }
                    }
                    break;
                case header.chunk:
                    {
                        int id = pr.ReadInt32();
                        long index = pr.ReadInt64();
                        int size = pr.ReadInt32();
                        byte[] buffer = pr.ReadBytes(size);

                        TransferQueue transferQueue = _transfers[id];

                        transferQueue.write(buffer, index);
                        transferQueue.progress =  (int)(transferQueue.transferred * 100 / transferQueue.length);
                        if(transferQueue.lastProgress < transferQueue.progress)
                        {
                            transferQueue.lastProgress = transferQueue.progress;
                            if(null != progressChanged)
                            {
                                progressChanged(this, transferQueue);
                            }

                            if (transferQueue.progress == 100)
                            {
                                transferQueue.close();
                                if(null != complete)
                                {
                                    complete(this, transferQueue);
                                }
                            }

                        }
                    }
                    break;
            }
            pr.Dispose();
        }

        private void receiveCallBack(IAsyncResult ar)
        {
            try
            {
                int found = _baseSocket.EndReceive(ar);
                if (found >= 4)
                {
                    _baseSocket.Receive(_buffer, 0, 4, SocketFlags.None);
                    int size = BitConverter.ToInt32(_buffer, 0);
                    int read = _baseSocket.Receive(_buffer, 0, size, SocketFlags.None);
                    while (read < size)
                    {
                        read += _baseSocket.Receive(_buffer, read, size - read, SocketFlags.None);
                    }
                    process();
                }
                run();
            }
            catch
            {
                close();
            }
        }

        internal void callProgressChanged(TransferQueue queue)
        {
            if(progressChanged != null)
            {
                progressChanged(this, queue);
            }
        }
    }
}
