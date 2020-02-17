using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace olping
{
    public enum NetSystemStatus
    {
        HostInitializing,
        HostWaitingForGameSession,
        HostWaitingForPlayers,
        HostInMatch
    }

    class Program
    {
        static void Main(string[] args)
        {
            UdpClient remoteSocket;
            int remotePort = 8001;

            if (args.Length < 1) {
                Console.WriteLine("missing server ip/hostname argument");
                return;
            }

            string server = args[0];

            if (!IPAddress.TryParse(server, out IPAddress addr))
            {
                var addrs = Dns.GetHostAddresses(server);
                addr = addrs == null || addrs.Length == 0 ? null : addrs[0];
            }

            try
            {
                remoteSocket = new UdpClient();
                remoteSocket.Connect(new IPEndPoint(addr, remotePort));
            }
            catch (SocketException ex)
            {
                throw new Exception("Cannot connect to UDP endpoint " + addr + ":" + remotePort + ". Is a firewall blocking the connection? " + ex.Message);
            }
            var packet = new byte[19 + 4 + 4 + 8 + 4];

            long nextTime = 0;
            var taskRemote = remoteSocket.ReceiveAsync();
            int idx = 0;
            long lastRecvTime = 0;
            long lastSendTime = 0;
            int lastSendIdx = 0;
            int interval = 1000;
            int timeout = 1000;
            for (;;) {
                long time = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (time >= nextTime) {
                    if (lastSendTime - lastRecvTime >= timeout)
                        Console.WriteLine(addr + " " + lastSendIdx + " timed out");
                    idx++;
                    Array.Copy(BitConverter.GetBytes(-1), 0, packet, 0, 4);
                    Array.Copy(BitConverter.GetBytes(0), 0, packet, 8, 4);
                    Array.Copy(BitConverter.GetBytes(0), 0, packet, 19, 4);
                    Array.Copy(BitConverter.GetBytes(0), 0, packet, 19 + 4, 4);
                    Array.Copy(BitConverter.GetBytes(time), 0, packet, 19 + 4 + 4, 8);
                    Array.Copy(BitConverter.GetBytes(idx), 0, packet, 19 + 4 + 4 + 8, 4);
                    uint hash = xxHashSharp.xxHash.CalculateHash(packet);
                    Array.Copy(BitConverter.GetBytes(hash), 0, packet, 8, 4);
                    remoteSocket.Send(packet, packet.Length);
                    lastSendTime = time;
                    lastSendIdx = idx;
                    nextTime = time + interval;
                } else if (taskRemote.Wait((int)(nextTime - time))) {
                    time = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    var result = taskRemote.Result;
                    var ret = result.Buffer;

                    uint hash = BitConverter.ToUInt32(ret, 8);
                    Array.Copy(BitConverter.GetBytes(0), 0, ret, 8, 4);
                    uint srcHash = xxHashSharp.xxHash.CalculateHash(ret);
                    if (srcHash != hash) // ignore unknown hash
                        continue;

                    var status = (NetSystemStatus)ret[19 + 4];
                    var lastTime = BitConverter.ToInt64(ret, 19 + 4 + 4);
                    var lastIdx = BitConverter.ToInt32(ret, 19 + 4 + 4 + 8);
                    taskRemote = remoteSocket.ReceiveAsync();
                    lastRecvTime = time;
                    Console.WriteLine(addr + " sequence: " + lastIdx + " status: " + status + " time: " + (time - lastTime) + "ms");
                }
            }
        }
    }
}
