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
using System.IO;
using System.Security.Cryptography;

namespace DHT
{
    class Program
    {
        static void Main(string[] args)
        {
            //var dom = new Random();
            //var bytes1 = new byte[20];
            //var bytes2 = new byte[20];
            //dom.NextBytes(bytes1);
            //dom.NextBytes(bytes2);
            //var a1 = bytes1.ToHexString();
            //Console.WriteLine(a1);
            //var a2 = bytes2.ToHexString();
            //Console.WriteLine(a2);
            //var bytes3 = new List<byte>();
            //bytes3.AddRange(bytes1.Take(10));
            //bytes3.AddRange(bytes2.Skip(10).Take(10));
            //var bytes33 = bytes3.ToArray();
            //var a3 = bytes33.ToHexString();
            //Console.WriteLine(a3);


            var udpServer = new UDPServer(6882, IPAddress.Any);
            udpServer.Run();
            udpServer.ReJoin();
            udpServer.SendFindNode();


            Console.ReadLine();
        }
    }
    public class Helpers
    {
        private static Random r = new Random();
        private static SHA1 sha1 = new SHA1CryptoServiceProvider();
        public static byte[] GetTil() { var result = new byte[2]; r.NextBytes(result); return result; }
        public static byte[] GetRandomID()
        {
            var result = new byte[20];
            r.NextBytes(result);
            lock (sha1)
            {
                result = sha1.ComputeHash(result);
                return result;
            }
        }

    }
    public class UDPServer
    {
        byte[] localID = null;
        Socket sock = null;
        public UDPServer(int port, IPAddress addr)
        {
            localID = GetRandomID();
            this.sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            const long IOC_IN = 0x80000000;
            const long IOC_VENDOR = 0x18000000;
            const long SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            byte[] optionInValue = { Convert.ToByte(false) };
            byte[] optionOutValue = new byte[4];
            this.sock.IOControl((IOControlCode)SIO_UDP_CONNRESET, optionInValue, optionOutValue);
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
                //Console.WriteLine(ex.ToString());
            }
            bool notsuccess = false;
            do
            {
                try
                {
                    this.sock.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteAddrss, EndRecv, null);
                    notsuccess = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    notsuccess = true;
                }
            } while (notsuccess);
        }
        BencodeParser parser = new BencodeParser();
        private void OnRecvMessage(byte[] data, IPEndPoint ipinfo)
        {
            try 
            {
                var dic = parser.Parse<BDictionary>(data);
                if(dic.Get<BString>("y") == "r")
                {
                    var respNode = dic.Get<BDictionary>("r");
                    var nodeList = respNode.Get<BString>("nodes");
                    if (nodeList == null || nodeList.Length == 0)
                    {
                        //Console.WriteLine("ping response " + ipinfo.ToString());
                        return;
                    }
                    if (nodeList.Value.Count % 26 != 0)
                        throw new Exception("sd");
                    var tt = nodeList.Value.ToArray();
                    var ssss = ParseList(tt);
                    foreach (var t in ssss)
                    {
                        lock(this.nodes)
                        {
                            this.nodes.Enqueue(t);
                            //Console.WriteLine("find a node " + t.Item2);
                        }
                    }
                    return;
                }
                if(dic.Get<BString>("y") == "q" && dic.Get<BString>("q") == "ping")
                {
                    //Console.WriteLine("ping");
                    BDictionary resultDic = new BDictionary();
                    resultDic.Add("t", dic.Get<BString>("t"));
                    resultDic.Add("y", "r");
                    var r = new BDictionary();
                    r.Add("id", new BString(this.localID));
                    resultDic.Add("r", r);
                    var dataresult = resultDic.EncodeAsBytes();
                    this.sock.BeginSendTo(dataresult, 0, dataresult.Length, SocketFlags.None, ipinfo, (ar) => { this.sock.EndSendTo(ar); }, null);
                    return;
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
                    var neighbor = new List<byte>();
                    neighbor.AddRange(info_hash.Value.Take(10));
                    neighbor.AddRange(this.localID.Skip(10).Take(10));
                    r.Add("id", new BString(neighbor));
                    r.Add("token", new BString(info_hash.Value.Take(2)));
                    r.Add("nodes", "");
                    result.Add("r", r);
                    var dataresult = result.EncodeAsBytes();
                    this.sock.BeginSendTo(dataresult, 0, dataresult.Length, SocketFlags.None, ipinfo, (ar) => { this.sock.EndSendTo(ar); }, null);
                    WriteInfo("get_peers", info_hash.Value.ToArray(), rid.Value.ToArray(), null);
                    Console.WriteLine(info_hash.Value.ToArray().ToHexString() + "|" + ipinfo.ToString());
                    return;
                    //this.InfoHashList.Add(info_hash.Value.ToArray());

                }
                if (dic.Get<BString>("y") == "q" && dic.Get<BString>("q") == "announce_peer")
                {
                    var id = dic.Get<BString>("id");
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
                    WriteInfo("announce_peer", info_hash.Value.ToArray(), id.Value.ToArray(), new IPEndPoint(ipinfo.Address, port));
                    return;
                }
                //Console.WriteLine("unknow pack");
                //Console.WriteLine(dic.Keys.ToString());
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        private IPEndPoint defaultIP = new IPEndPoint(IPAddress.Loopback, 0);
        private void WriteInfo(string commandName, byte[] info_hash, byte[] nid, IPEndPoint ipadd)
        {
            var str = $"{commandName}:{info_hash.ToHexString()}:{nid.ToHexString()}:{(ipadd ?? defaultIP).ToString()}";
            var fswriter = new StreamWriter(new FileStream("./logFile", FileMode.Append));
            fswriter.WriteLine(str);
            fswriter.Flush();
            fswriter.Close();
        }
        private List<Tuple<byte[], IPEndPoint>> ParseList(byte[] data)
        {
            var result = new List<Tuple<byte[], IPEndPoint>>();
            for(int i = 0; i < data.Length; i += 26)
            {
                var dd = data.Skip(i).Take(26).ToArray();
                //var bc = dd.ToHexString();
                //Console.WriteLine(bc);
                var b = dd[24];
                dd[24] = dd[25];
                dd[25] = b;
                var id = dd.Take(20).ToArray();
                var ip = new IPAddress(dd.Skip(20).Take(4).ToArray());
                var port = BitConverter.ToUInt16(dd, 24);
                var tt = new Tuple<byte[], IPEndPoint>(id, new IPEndPoint(ip, port));
                result.Add(tt);
            }
            return result;
        }
        public Queue<Tuple<byte[], IPEndPoint>> nodes = new Queue<Tuple<byte[], IPEndPoint>>();

        public void ReJoin()
        {
            new Thread(()=> {
                var hosts = new List<string>()
                        {
                            "router.bittorrent.com",
                            "dht.transmissionbt.com",
                            "router.utorrent.com"
                        }.Select(x => Dns.Resolve(x).AddressList[0]).ToList();
                while (true)
                {
                    int count = 0;
                    lock(this.nodes)
                    {
                        count = this.nodes.Count;
                    }
                    if(count == 0)
                    {
                        
                        foreach (var t in hosts)
                        {
                            SendFindNode(null, new IPEndPoint(t, 6881));
                        }
                    }
                    Thread.Sleep(3 * 1000);
                }
            }).Start();
        }
        public void SendFindNode()
        {
            while(true)
            {
                Tuple<byte[], IPEndPoint> result = null;
                lock(nodes)
                {
                    if (nodes.Count > 0)
                    {
                        result = nodes.Dequeue();
                    }
                }
                if(result != null)
                {
                    SendFindNode(result.Item1, result.Item2);
                }
                Thread.Sleep((int)((1/50/5)*1000));
            }
        }
        private byte[] get_neighbor(byte[] target, byte[] nid)
        {
            var result = new byte[20];
            for (int i = 0; i < 10; i++)
                result[i] = target[i];
            for (int i = 10; i < 20; i++)
                result[i] = nid[i];
            return result;
        }
        private void SendFindNode(byte[] data, IPEndPoint address, byte[] aaa = null, byte[] ttid = null)
        {
            byte[] nid = null;
            if(data == null)
            {
                nid = this.localID;
            }
            else
            {
                nid = get_neighbor(data, this.localID);
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
            this.sock.BeginSendTo(dataarrar, 0, dataarrar.Length, SocketFlags.None, address, (x) => { this.sock.EndReceive(x); }, null);
        }
        static byte[] GetRandomID()
        {
            var r = new Random();
            byte[] result = new byte[20];
            r.NextBytes(result);
            return result;
        }
    }
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
        private List<Tuple<int, byte[]>> TorList = new List<Tuple<int, byte[]>>();
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
    public static class HexHelper
    {
        public static string ToHexString(this byte[] data)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var b in data)
            {
                var t = b / 16;
                sb.Append((char)(t + (t <= 9 ? '0' : 'W')));
                var f = b % 16;
                sb.Append((char)(f + (f <= 9 ? '0' : 'W')));
            }

            return sb.ToString();
        }
        public static string ToHexString2(this byte[] data) => string.Join("", data.SelectMany((x) => new int[] { x / 16, x % 16 }).ToList().Select(x => (char)(x + (x <= 9 ? '0' : 'W'))));
        public static byte[] ParseToHex(this string data)
        {
            if (data.Length % 2 != 0)
                throw new Exception();
            data = data.ToLower();
            List<int> temp = new List<int>();
            foreach (var c in data)
            {
                var t = c - '0';
                if (t < 0)
                    throw new Exception();
                else if (t > 9)
                {
                    t = t - ('a' - '0');
                    if (t < 0 || t > 5)
                        throw new Exception();
                    temp.Add(t + 10);
                }
                else
                    temp.Add(t);
            }
            return temp.Tuken().Select(x => (byte)(x.Item1 * 16 + x.Item2)).ToArray();
        }
        public static IEnumerable<(T, T)> Tuken<T>(this IEnumerable<T> array)
        {
            if (array.Count() % 2 != 0)
                throw new Exception();
            for (int i = 0, len = array.Count(); i < len; i += 2)
            {
                var cc = array.Skip(i).Take(2).ToList();
                yield return (cc[0], cc[1]);
            }
        }

        public static byte[] ToDicBytes(this Dictionary<string, object> temp)
        {
            var result = new List<byte>();
            result.Add((byte)'d');
            foreach(var tempItem in temp)
            {
                var key = tempItem.Key;
                if(tempItem.Value.GetType() == typeof(string))
                {
                    result.AddRange(Encoding.ASCII.GetBytes($"{key.Length}:{key}"));
                    var value = tempItem.Value as string;
                    result.AddRange(Encoding.ASCII.GetBytes($"{value.Length}:{value}"));
                }
                if(tempItem.Value.GetType() == typeof(byte[]))
                {
                    result.AddRange(Encoding.ASCII.GetBytes($"{key.Length}:{key}"));
                    var value = tempItem.Value as byte[];
                    result.AddRange(Encoding.ASCII.GetBytes($"{value.Length}:"));
                    result.AddRange(value);
                }
                if(tempItem.Value.GetType() == typeof(Dictionary<string, object>))
                {
                    result.AddRange(Encoding.ASCII.GetBytes($"{key.Length}:{key}"));
                    var val = ToDicBytes((Dictionary<string, Object>)tempItem.Value);
                    result.AddRange(val);
                }
            }
            result.Add((byte)'e');
            return result.ToArray();
        }
    }
}
