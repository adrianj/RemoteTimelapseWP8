using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using RemoteTimelapseWP8;

namespace DummyPhoneApp
{
    class DummyPhoneApp : IDisposable
    {
        public string DeviceName { get; set; }
        public int UdpPcPort { get; set; }
        public int UdpPhonePort { get; set; }
        public int TcpPhonePort { get; set; }

        long framePeriod = 2000;
        public long FramePeriodInMilliseconds { get { return framePeriod; } set { if (value > 500 && value < 100000) framePeriod = value; } }
        public long FramePeriodInTicks { get { return FramePeriodInMilliseconds * 10000; } }

        volatile bool imageCapturing = false;

        public DummyPhoneApp()
        {
            DeviceName = "DummyPhone";
            UdpPcPort = AppConstants.DefaultUdpPcPort;
            UdpPhonePort = AppConstants.DefaultUdpPhonePort;
            TcpPhonePort = AppConstants.DefaultTcpPhonePort;
        }

        private void ProcessControlMessage(byte[] bytes, int len)
        {
            string message = ASCIIEncoding.ASCII.GetString(bytes, 0, len);
            Console.WriteLine(DeviceName + " Received: " + message);
            imageCapturing = true;
            connection.SendBytes(bytes, 0, len);
        }


        private void CaptureImage()
        {
            if (imageCapturing)
            {
                string message = "SHOOT!";
                Console.WriteLine(message);
                byte[] bytes = ASCIIEncoding.ASCII.GetBytes(message);
                connection.SendBytes(bytes, 0, bytes.Length);
            }
        }

        #region TCP

        TcpConnection connection = new TcpConnection();
        byte[] tcpReceiveBuffer = new byte[1024];
        Thread ImageThread;

        public void ListenToTcp()
        {
            connection.Listen(AppConstants.DefaultTcpPhonePort);
            connection.ReceiveDataAvailable += (o, e) =>
            {
                int len = connection.ReadBytes(tcpReceiveBuffer, 0, tcpReceiveBuffer.Length);
                ProcessControlMessage(tcpReceiveBuffer, len);
            };
            ImageThread = new Thread(new ThreadStart(CaptureImageThread));
            ImageThread.Start();
        }

        volatile bool stopImageThread = false;
        void CaptureImageThread()
        {
            Console.WriteLine("Starting image thread");
            long then = DateTime.Now.Ticks;
            while (!stopImageThread)
            {
                Thread.Sleep(100);
                long now = DateTime.Now.Ticks;
                if (now - then >= FramePeriodInTicks)
                {
                    if(connection.IsConnected)
                        CaptureImage();
                    then = now;
                }
            }
        }

        void StopTcp()
        {
            stopImageThread = true;
            connection.Disconnect();
        }




        #endregion

        #region UDP

        public void StopUdp() { stopUdpThread = true; }
        volatile bool stopUdpThread = false;
        Thread UdpThread;

        public void ListenToUdp()
        {
            UdpThread = new Thread(new ThreadStart(UdpListeningThread));
            UdpThread.Start();
        }

        volatile bool udpMessageReceived = false;

        void UdpListeningThread()
        {
            Console.WriteLine("Listening on UDP port " + UdpPhonePort);
            IPEndPoint pcEp = new IPEndPoint(IPAddress.Any, UdpPhonePort);
            UdpClient udp = new UdpClient(UdpPhonePort);
            

            while(!stopUdpThread)
            {
                udpMessageReceived = false;
                udp.BeginReceive(new AsyncCallback(UdpReceiveCallback), udp);
                while(!udpMessageReceived && !stopUdpThread)
                {
                    Thread.Sleep(500);
                }
            }
        }

        void UdpReceiveCallback(IAsyncResult ar)
        {
            
            UdpClient udp = (ar.AsyncState as UdpClient);
            IPEndPoint ep = ar.AsyncState as IPEndPoint;
            byte[] bytes = udp.EndReceive(ar, ref ep);
            if(ep.Port == this.UdpPcPort)
            {
                string message = ASCIIEncoding.ASCII.GetString(bytes);
                Console.WriteLine("UDP Received: '" + message+"' from "+ep);
                if(message.Equals(DeviceFinder.RequestMessage))
                {
                    bytes = ASCIIEncoding.ASCII.GetBytes(this.DeviceName);
                    udp.Send(bytes, bytes.Length, ep);
                }
            }
            udpMessageReceived = true;
        }
        #endregion

        bool disposed = false;
        public void Dispose()
        {
            if(!disposed)
            {
                StopUdp();
                StopTcp();
                disposed = true;
            }
        }

        static void Main(string[] args)
        {
            List<string> argList = new List<string>(args);
            bool test = false;
            if(argList.Contains("-t"))
            {
                test = true;
                argList.Remove("-t");
            }
            string name = "DummyPhone";
            if(args.Length > 0)
                name = args[0];

            using (DummyPhoneApp app = new DummyPhoneApp() { DeviceName = name })
            {

                app.ListenToUdp();
                app.ListenToTcp();
                Console.WriteLine(name+": App Running.");
                if(test)
                    TestUdpCommunications();
                Console.WriteLine(name+": Press any key to quit...");
                Console.ReadKey();
                Console.WriteLine(name+ ": Waiting for threads to finish...");
            }
        }

        static void TestUdpCommunications()
        {
            DeviceFinder deviceFinder = new DeviceFinder();
            RemoteDevice[] devices = deviceFinder.FindDevices();
            if (devices.Length < 1)
                Console.WriteLine("Failed to find any devices.");
            else
            {
                foreach (RemoteDevice device in devices)
                    Console.WriteLine("" + device);
                using (TcpConnection connection = new TcpConnection())
                {
                    byte [] buffer = new byte[1024];
                    connection.ReceiveDataAvailable += (o, e) =>
                    {
                        int len = connection.ReadBytes(buffer, 0, buffer.Length);
                        string msg = ASCIIEncoding.ASCII.GetString(buffer, 0, len);
                        Console.WriteLine("PC Rx: " + msg);
                    };
                    connection.Connect(devices[0].TcpEndPoint);
                    byte[] bytes = ASCIIEncoding.ASCII.GetBytes("Message from PC 1!");
                    int l = connection.SendBytes(bytes, 0, bytes.Length);
                    Console.WriteLine("PC Sent " + l + " bytes");
                    Thread.Sleep(1000);
                    l = connection.SendBytes(bytes, 0, bytes.Length);
                    Console.WriteLine("PC Sent " + l + " bytes");
                    Thread.Sleep(1000);
                }
                Thread.Sleep(1000);
                Console.WriteLine("Try it again");
                Thread.Sleep(1000);
                using (TcpConnection connection = new TcpConnection())
                {
                    byte[] buffer = new byte[1024];
                    connection.ReceiveDataAvailable += (o, e) =>
                    {
                        int len = connection.ReadBytes(buffer, 0, buffer.Length);
                        string msg = ASCIIEncoding.ASCII.GetString(buffer, 0, len);
                        Console.WriteLine("PC Rx: " + msg);
                    };
                    connection.Connect(devices[0].TcpEndPoint);
                    byte[] bytes = ASCIIEncoding.ASCII.GetBytes("Message from PC 2!");
                    int l = connection.SendBytes(bytes, 0, bytes.Length);
                    Console.WriteLine("PC Sent " + l + " bytes");
                    Thread.Sleep(1000);
                    l = connection.SendBytes(bytes, 0, bytes.Length);
                    Console.WriteLine("PC Sent " + l + " bytes");
                    Thread.Sleep(1000);
                }
            }
        }
    }
}
