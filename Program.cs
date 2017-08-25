﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BencodeNET.Parsing;
using BencodeNET.Objects;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace DHT
{
    class Program
    {
        static void Main(string[] args)
        {
            var udpServer = new UDPServer(15222, IPAddress.Any);
            udpServer.Run();
            udpServer.SendFindNode();
            Console.ReadLine();
        }
    }
    public class Helpers
    {
        private static Random r = new Random();
        public static byte[] GetTil() { var result = new byte[2]; r.NextBytes(result); return result; }
        public static byte[] GetRandomID() { var result = new byte[20]; r.NextBytes(result); return result; }

    }
    public class UDPServer
    {
        byte[] localID = null;
        Socket sock = null;
        public UDPServer(int port, IPAddress addr)
        {
            localID = Helpers.GetRandomID();
            this.sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            this.sock.Bind(new IPEndPoint(addr, port));
        }
        byte[] buffer = new byte[65535];
        public void Run()
        {
            EndPoint remoteAddress = new IPEndPoint(IPAddress.Loopback, 0);
            this.sock.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteAddress, EndRecv, null);
        }
        private void EndRecv(IAsyncResult result)
        {
            EndPoint remoteAddrss = new IPEndPoint(IPAddress.Loopback, 0);
            try
            {
                int count = this.sock.EndReceiveFrom(result, ref remoteAddrss);
                if (count > 0)
                    OnRecvMessage(this.buffer.Take(count).ToArray(), (IPEndPoint)remoteAddrss);

            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            EndPoint newIp = new IPEndPoint(IPAddress.Loopback, 0);
            this.sock.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteAddrss, EndRecv, null);
        }
        BencodeParser parser = new BencodeParser();
        private void OnRecvMessage(byte[] data, IPEndPoint ipinfo)
        {
            try 
            {
                var dic = parser.ParseString<BDictionary>(Encoding.ASCII.GetString(data));
                if(dic.Get<BString>("y") == "r")
                {
                    var respNode = dic.Get<BDictionary>("r");
                    var nodeList = respNode.Get<BString>("nodes");
                    if (nodeList == null || nodeList.Length == 0)
                    {
                        Console.WriteLine("ping response " + ipinfo.ToString());
                        return;
                    }
                    if (nodeList.Value.Count % 26 != 0)
                        throw new Exception("sd");
                    var tt = nodeList.Value.ToArray();
                    var ssss = ParseList(tt);
                    foreach (var t in ssss.Distinct())
                    {
                        lock(this.nodes)
                        {
                            this.nodes.Enqueue(t);
                            Console.WriteLine("find a node " + t.Item2);
                        }
                    }
                }
                if(dic.Get<BString>("y") == "q" && dic.Get<BString>("q") == "ping")
                {
                    BDictionary resultDic = new BDictionary();
                    resultDic.Add("t", dic.Get<BString>("t"));
                    resultDic.Add("y", "r");
                    var r = new BDictionary();
                    r.Add("id", new BString(this.localID));
                    resultDic.Add("r", r);
                    var dataresult = resultDic.EncodeAsBytes();
                    this.sock.BeginSendTo(dataresult, 0, dataresult.Length, SocketFlags.None, ipinfo, (ar) => { this.sock.EndSendTo(ar); }, null);
                }
                //if(dic.Get<BString>("y") == "q" && dic.Get<BString>("q") == "find_node")
                //{
                //
                //}
                if(dic.Get<BString>("y") == "q" && dic.Get<BString>("q") == "get_peers")
                {
                    var t = dic.Get<BString>("t");
                    var a = dic.Get<BDictionary>("a");
                    var rid = a.Get<BString>("id");
                    var info_hash = a.Get<BString>("info_hash");
                    var result = new BDictionary();
                    result.Add("t", t);
                    result.Add("y", "r");
                    var r = new BDictionary();
                    var random = new byte[20];
                    new Random().NextBytes(random);
                    r.Add("id", new BString(random));
                    r.Add("token", new BString(info_hash.Value.Take(2)));
                    r.Add("nodes", "");
                    result.Add("r", r);
                    var dataresult = result.EncodeAsBytes();
                    this.sock.BeginSendTo(dataresult, 0, dataresult.Length, SocketFlags.None, ipinfo, (ar) => { this.sock.EndSendTo(ar); }, null);
                    //this.InfoHashList.Add(info_hash.Value.ToArray());

                }
                if (dic.Get<BString>("y") == "q" && dic.Get<BString>("q") == "announce_peer")
                {
                    var a = dic.Get<BDictionary>("a");
                    var info_hash = a.Get<BString>("info_hash");
                    int port = 0;
                    if (a.ContainsKey("implied_port") && a.Get<BNumber>("implied_port") == 1)
                    {
                        port = ipinfo.Port;
                    }
                    else
                        port = a.Get<BNumber>("port");
                    //CanDownload.Add(new Tuple<byte[], IPEndPoint>(info_hash.Value.ToArray(), new IPEndPoint(ipinfo.Address, port)));
                    Console.WriteLine("find a hash_info_candownload!!-------------------------------------");
                }
            }
            catch
            {

            }
        }
        private List<Tuple<byte[], IPEndPoint>> ParseList(byte[] data)
        {
            var result = new List<Tuple<byte[], IPEndPoint>>();
            for(int i = 0; i < data.Length / 26; i++)
            {
                var dd = data.Skip(i * 26).Take(26).ToArray();
                var b = dd[24];
                dd[24] = dd[25];
                dd[25] = b;
                result.Add(new Tuple<byte[], IPEndPoint>(dd.Take(20).ToArray(), new IPEndPoint(new IPAddress(dd.Skip(20).Take(4).ToArray()), BitConverter.ToUInt16(dd, 24))));
            }
            return result;
        }
        public Queue<Tuple<byte[], IPEndPoint>> nodes = new Queue<Tuple<byte[], IPEndPoint>>();
        public void SendFindNode()
        {
            var hosts = new List<string>()
            {
                "router.bittorrent.com",
                "dht.transmissionbt.com",
                "router.utorrent.com"
            }.Select(x => Dns.Resolve(x).AddressList[0]).ToList();
            while(true)
            {
                Tuple<byte[], IPEndPoint> result = null;
                lock(nodes)
                {
                    if (nodes.Count > 0)
                        result = nodes.Dequeue();
                }
                if(result == null)
                {
                    foreach(var x in hosts)
                    {
                        SendFindNode(null, new IPEndPoint(x, 6881));
                    }
                }
                else
                {
                    SendFindNode(result.Item1, result.Item2);
                }
                Thread.Sleep(50);
            }
        }
        private void SendFindNode(byte[] data, IPEndPoint address)
        {
            byte[] nid = null;
            if(data == null)
            {
                nid = this.localID;
            }
            else
            {
                var newid = new List<byte>();
                newid.AddRange(data.Take(10));
                newid.AddRange(this.localID.Skip(10).Take(10));
                nid = newid.ToArray();
            }
            var tid = Helpers.GetTil();
            BDictionary sendData = new BDictionary();
            sendData.Add("t", new BString(tid));
            sendData.Add("y", "q");
            sendData.Add("q", "find_node");
            var a = new BDictionary();
            a.Add("id", new BString(nid));
            a.Add("target", new BString(Helpers.GetRandomID()));
            sendData.Add("a", a);
            Send(sendData, address);
        }
        private void Send(BDictionary data, IPEndPoint address)
        {
            var dataarrar = data.EncodeAsBytes();
            this.sock.SendTo(dataarrar, address);
        }
        static byte[] GetRandomID()
        {
            var r = new Random();
            byte[] result = new byte[20];
            r.NextBytes(result);
            return result;
        }
    }
    //public class BittorrentDownloader
    //{
    //    public IPEndPoint Ipaddress = null;
    //    public byte[] InfoHash = null;
    //    public byte[] NodeID = null;
    //    Socket sock = null;
    //    public BittorrentDownloader(IPEndPoint ipinfo, byte[] infohash, byte[] nodeid)
    //    {
    //        this.Ipaddress = ipinfo;
    //        this.InfoHash = infohash;
    //        this.NodeID = nodeid;
    //    }

    //    public void StartWork()
    //    {
    //        this.sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    //        var con_event = new SocketAsyncEventArgs() { RemoteEndPoint = this.Ipaddress };
    //        con_event.Completed += con_event_Completed;
    //        if (!this.sock.ConnectAsync(con_event))
    //        {
    //            ConnectCallback(con_event, sock);
    //            SocketAsyncEventArgs recvEventArgs = new SocketAsyncEventArgs();
    //            recvEventArgs.BufferList = new List<ArraySegment<byte>>() { new ArraySegment<byte>(new byte[4096]) };
    //            recvEventArgs.Completed += recvEventArgs_Completed;
    //            if(!this.sock.ReceiveAsync(recvEventArgs))
    //            {
    //                RecvCallBack(recvEventArgs, sock);
    //            }
    //        }
    //    }
    //    private void RecvCallBack(SocketAsyncEventArgs e, Socket s)
    //    {
    //        try
    //        {
    //            // do sth when recvData
    //            var recucount = e.BytesTransferred;
    //        }
    //        catch
    //        {

    //        }
    //        if(!this.sock.ReceiveAsync(e))
    //        {
    //            RecvCallBack(e, s);
    //        }
    //    }
    //    void recvEventArgs_Completed(object sender, SocketAsyncEventArgs e)
    //    {
    //        RecvCallBack(e, sock);
    //    }
    //    private void ConnectCallback(SocketAsyncEventArgs e, Socket s)
    //    {
    //        //do sth when connected
    //    }
    //    void con_event_Completed(object sender, SocketAsyncEventArgs e)
    //    {
    //        ConnectCallback(e, this.sock);
    //    }
    //}
    public class BittorrentDownloader
    {
        public IPEndPoint Ipaddress = null;
        public byte[] InfoHash = null;
        public byte[] NodeID = null;
        Socket sock = null;
        public BittorrentDownloader(IPEndPoint ipinfo, byte[] infohash, byte[] nodeid)
        {
            this.Ipaddress = ipinfo;
            this.InfoHash = infohash;
            this.NodeID = nodeid;
        }
        public void Run()
        {
            this.sock.BeginConnect(this.Ipaddress, new AsyncCallback(EndConnect), null);
        }
        private void SendShakeHand()
        {
            var peer = Helpers.GetRandomID();
            var list = new List<byte>();
            list.Add(0x13);
            list.AddRange(Encoding.ASCII.GetBytes("BitTorrent protocol"));
            list.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00 });
            list.AddRange(this.InfoHash);
            list.AddRange(peer);
            this.sock.Send(list.ToArray(), SocketFlags.None);
        }
        byte[] buffer = new byte[4096];
        List<byte> DataBuffer = new List<byte>();
        private void EndConnect(IAsyncResult result)
        {
            try
            {
                this.sock.EndConnect(result);
                this.sock.BeginReceive(this.buffer, 0, this.buffer.Length, SocketFlags.None, new AsyncCallback(EndRecvData), null);
                this.SendShakeHand();
            }
            catch
            {

            }
        }
        int state = 0;
        private void EndRecvData(IAsyncResult result)
        {
            var recvCount = this.sock.EndReceive(result);
            this.DataBuffer.AddRange(buffer.Take(recvCount));
            check();
            if(this.state == 1)
            {

            }
            this.sock.BeginReceive(this.buffer, 0, this.buffer.Length, SocketFlags.None, EndRecvData, null);
        }
        private void SendExtShakeHand()
        {
            BDictionary supose = new BDictionary();
            supose.Add("ut_metadata", new BNumber(1));
            BDictionary dic = new BDictionary();
            dic.Add("m", supose);
            var datalist = new List<byte>();
            datalist.Add(0x14);
            datalist.Add(0x00);
            datalist.AddRange(dic.EncodeAsBytes());
            datalist.InsertRange(0, BitConverter.GetBytes((UInt32)datalist.Count).Reverse());
            this.sock.Send(datalist.ToArray());
        }
        private bool check()
        {
            if(this.state == 0)
            {
                if (this.DataBuffer[0] != 0x13)
                    throw new Exception();
                var str1 = Encoding.ASCII.GetString(this.DataBuffer.ToArray(), 1, 19);
                if (str1 != "BitTorrent protocol")
                    throw new Exception();
                var reserved = this.DataBuffer.Skip(20).Take(8).ToArray();
                var infoHash = this.DataBuffer.Skip(28).Take(20).ToArray();
                if (infoHash != this.InfoHash)
                {
                    throw new Exception();
                }
                var peer_id = this.DataBuffer.ToArray().Skip(48).Take(20);
                this.state = 1;
                SendExtShakeHand();
                this.DataBuffer.RemoveRange(0, 68);
                return true;
            }
            else if(this.state == 1)
            {
                var lengthArray = this.DataBuffer.Take(4);
                var len = BitConverter.ToUInt32(lengthArray.Reverse().ToArray(), 0);
                if(this.DataBuffer.Count >= (4 + len))
                {
                    var dataBuf = this.DataBuffer.Skip(4).Take((int)len).ToArray();
                    var msgid = dataBuf[0];
                    if (msgid != 0x08)
                        throw new Exception();
                    var extendMsgID = dataBuf[1];
                    if (extendMsgID != 0x00)
                        throw new Exception();
                    BencodeParser parse = new BencodeParser();
                    var supose = parse.Parse<BDictionary>(dataBuf.Skip(2).ToArray());
                    if (!supose.ContainsKey("m"))
                        throw new Exception();
                    BDictionary result = null;
                    var suplist = supose.Get<BDictionary>("m");
                    if (!suplist.ContainsKey("ut_metadata"))
                        throw new Exception();
                    var numid = suplist.Get<BNumber>("ut_metadata");
                    var size = supose.Get<BNumber>("metadata_size");
                    var count = size / (16 * 1024) + (size % (16 * 1024) > 0 ? 1 : 0);
                    for(int i = 0 ; i < count; i++)
                    {
                        BDictionary data = new BDictionary();
                        data.Add("msg_type", 0);
                        data.Add("prece", i);
                        var data2 = new List<byte>();
                        data2.Add(0x14);
                        data2.Add((byte)numid.Value);
                        data2.AddRange(data.EncodeAsBytes());
                        data2.AddRange(BitConverter.GetBytes((UInt32)data2.Count).Reverse());
                        this.sock.Send(data2.ToArray());
                    }
                    this.state = 2;
                    return true;
                }
                else if(this.state == 2)
                {

                }
            }
            return false;
        }
    }
}