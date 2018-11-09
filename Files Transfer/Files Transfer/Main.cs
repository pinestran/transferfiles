using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Files_Transfer;

public partial class Main : Form
{
    private Listener listener;
    private TransferClient transferClient;
    private string outputFolder;
    private Timer tmrOverallProg;

    private bool serverRunning;
    public Main()
    {
        InitializeComponent();

        listener = new Listener();
        listener.Accepted += listener_Accepted;

        tmrOverallProg = new Timer();
        tmrOverallProg.Interval = 1000;
        tmrOverallProg.Tick += tmrOverallProg_Tick;

        outputFolder = "Transfers";
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        btnConnect.Click += new EventHandler(btnConnect_Click);
        btnStartServer.Click += new EventHandler(btnStartServer_Click);
        btnStopServer.Click += new EventHandler(btnStopServer_Click);
        btnSendFile.Click += new EventHandler(btnSendFile_Click);
        btnPauseTransfer.Click += new EventHandler(btnPauseTransfer_Click);
        btnStopTransfer.Click += new EventHandler(btnStopTransfer_Click);
        btnOpenDir.Click += new EventHandler(btnOpenDir_Click);
        btnClearComplete.Click += new EventHandler(btnClearComplete_Click);

        btnStopServer.Enabled = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        deregisterEvents();
        base.OnFormClosing(e);
    }

    private void tmrOverallProg_Tick(object sender, EventArgs e)
    {
        if (null == transferClient)
            return;
        progressOverall.Value = transferClient.getOverallProgress();
    }

    private void listener_Accepted(object sender, SocketAcceptedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new SocketAcceptedHandler(listener_Accepted), sender, e);
            return;
        }
        listener.Stop();
        TransferClient transferClient = new TransferClient(e.Accepted);
        transferClient.outputFolder = outputFolder;
        registerEvents();
        transferClient.run();
        tmrOverallProg.Start();
        setConnectionStatus(transferClient.endPoint.Address.ToString());

    }

    private void btnConnect_Click(object sender, EventArgs e)
    {
		if(null == transferClient)
        {
            transferClient = new TransferClient();
            transferClient.connect(txtCntHost.Text.Trim(), Int32.Parse(txtCntPort.Text.Trim()), connectCallBack);
            Enabled = false;
        }
        else
        {
            transferClient.close();
            transferClient = null;
        }
    }

    private void connectCallBack(object sender, string error)
    {
        if(InvokeRequired)
        {
            Invoke(new ConnectCallBack(connectCallBack), sender, error);
            return;
        }
        Enabled = true;
        if (null != error)
        {
            transferClient.close();
            transferClient = null;
            MessageBox.Show(error, "Connection Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        registerEvents();
        transferClient.outputFolder = outputFolder;
        transferClient.run();
        setConnectionStatus(transferClient.endPoint.Address.ToString());
        tmrOverallProg.Start();
        btnConnect.Text = "Disconnect";
    }

    private void registerEvents()
    {
        transferClient.queued += transferClient_Queued;
        transferClient.progressChanged += transferClient_ProgressChanged;
        transferClient.complete += transferClient_Complete;
        transferClient.stopped += transferClient_Stopped;
        transferClient.disconnected += transferClient_Disconnected;
    }

    private void transferClient_Stopped(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            Invoke(new TransferEventHandler(transferClient_Stopped), sender, queue);
            return;
        }
        lstTransfers.Items[queue.ID.ToString()].Remove();
    }

    private void transferClient_ProgressChanged(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            Invoke(new TransferEventHandler(transferClient_ProgressChanged), sender, queue);
            return;
        }
        lstTransfers.Items[queue.ID.ToString()].SubItems[3].Text = queue.progress + "%";
    }

    private void transferClient_Queued(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            Invoke(new TransferEventHandler(transferClient_Queued), sender, queue);
            return;
        }
        ListViewItem item = new ListViewItem();
        item.Text = queue.ID.ToString();
        item.SubItems.Add(queue.fileName);
        item.SubItems.Add(queue.type == QueueType.upload ? "upload" : "download");
        item.SubItems.Add("0%");
        item.Tag = queue;
        item.Name = queue.ID.ToString();
        lstTransfers.Items.Add(item);
        item.EnsureVisible();

        if(queue.type == QueueType.download)
        {
            transferClient.startTransfer(queue);
        }
    }

    private void transferClient_Disconnected(object sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new EventHandler(transferClient_Disconnected), sender, e);
            return;
        }
        deregisterEvents();
        foreach (ListViewItem item in lstTransfers.Items)
        {
            TransferQueue queue = (TransferQueue)item.Tag;
            queue.close();
        }
        lstTransfers.Items.Clear();
        progressOverall.Value = 0;
        transferClient = null;
        setConnectionStatus("-");
        if (serverRunning)
        {
            listener.Start(int.Parse(txtCntPort.Text.Trim()));
            setConnectionStatus("Waiting... ");
        }
        else
        {
            btnConnect.Text = "Connect";
        }
    }

    private void transferClient_Complete(object sender, TransferQueue queue)
    {
        System.Media.SystemSounds.Asterisk.Play();
    }

    private void deregisterEvents()
    {
        if (null == transferClient)
            return;
        transferClient.queued -= transferClient_Queued;
        transferClient.progressChanged -= transferClient_ProgressChanged;
        transferClient.complete -= transferClient_Complete;
        transferClient.stopped -= transferClient_Stopped;
        transferClient.disconnected -= transferClient_Disconnected;
    }

    private void setConnectionStatus(string connectedTo)
    {
        lblConnected.Text = "Connection: " + connectedTo;
    }

    private void btnStartServer_Click(object sender, EventArgs e)
    {
        if (serverRunning)
            return;
        serverRunning = true;
        try
        {
            listener.Start(int.Parse(txtCntPort.Text.Trim()));
            setConnectionStatus("Waiting... ");
            btnStartServer.Enabled = false;
            btnStopServer.Enabled = true;
        }
        catch
        {
            MessageBox.Show("Unable lisnten on port: " + txtCntPort.Text.Trim(), "", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

    }

    private void btnStopServer_Click(object sender, EventArgs e)
    {
        if (!serverRunning)
            return;
        if (null != transferClient)
            transferClient.close();
        serverRunning = false;
        listener.Stop();
        tmrOverallProg.Stop();
        setConnectionStatus("Stop Server");
        btnStartServer.Enabled = true;
        btnStopServer.Enabled = false;

    }

    private void btnClearComplete_Click(object sender, EventArgs e)
    {
        foreach (ListViewItem item  in lstTransfers.Items)
        {
            TransferQueue queue = (TransferQueue)item.Tag;
            if (queue.progress == 100 || !queue.running)
            {
                item.Remove();
            }
        }
    }

    private void btnOpenDir_Click(object sender, EventArgs e)
    {
        using (FolderBrowserDialog fbd = new FolderBrowserDialog())
        {
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                outputFolder = fbd.SelectedPath;
                if(transferClient != null)
                {
                    transferClient.outputFolder = outputFolder;
                }
                txtSaveDir.Text = outputFolder;
            }
        }
    }

    private void btnSendFile_Click(object sender, EventArgs e)
    {
        if (null == transferClient)
            return;
        using (OpenFileDialog o = new OpenFileDialog())
        {
            o.Filter = "All Files (*.*)|*.*";
            o.Multiselect = true;
            if (o.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                foreach(string file in o.FileNames)
                {
                    transferClient.queueTransfer(file);
                }
            }
        }
    }

    private void btnPauseTransfer_Click(object sender, EventArgs e)
    {
        if (null == transferClient)
            return;
        foreach (ListViewItem item in lstTransfers.SelectedItems)
        {
            TransferQueue queue = (TransferQueue)item.Tag;
            queue.client.stopTransfer(queue);
            item.Remove();
        }
        progressOverall.Value = 0;
    }

    private void btnStopTransfer_Click(object sender, EventArgs e)
    {
		
    }

    private void btnStartServer_Click_1(object sender, EventArgs e)
    {

    }
}