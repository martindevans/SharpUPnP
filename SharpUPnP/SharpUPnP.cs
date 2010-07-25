using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;

namespace SharpUPnP
{
    /// <summary>
    /// Provides Universal Plug & Play operations for NAT
    /// </summary>
    public class SharpUPnP
    {
        private static long timeoutTicks = new TimeSpan(0, 0, 0, 3).Ticks;
        /// <summary>
        /// The default timeout to use for operations. Threadsafe for reading and writing
        /// </summary>
        public static TimeSpan TimeOut
        {
            get { return new TimeSpan(Interlocked.Read(ref timeoutTicks)); }
            set
            {
                Interlocked.Exchange(ref timeoutTicks, value.Ticks);
            }
        }

        public static string DescUrl { get; private set; }
        public static string ServiceUrl { get; private set; }
        public static string EventUrl { get; private set; }

        private static object discoveryLock = new object();
        public static bool Discovered
        {
            get;
            private set;
        }
        public static bool UPnPAvailable
        {
            get;
            private set;
        }

        /// <summary>
        /// Discover if UPnP services are available
        /// </summary>
        /// <param name="rediscover">Indicates if discovery should be tried again if it has already been done</param>
        /// <returns>True, if UPnP servivesare available, otherwise false</returns>
        public static bool Discover(bool rediscover)
        {
            if (!Discovered || rediscover)
                return Discover();
            else
                return UPnPAvailable;
        }

        /// <summary>
        /// Discover available UPnP services available
        /// </summary>
        /// <returns>True, if UPnP servivesare available, otherwise false</returns>
        public static bool Discover()
        {
            lock (discoveryLock)
            {
                System.Net.NetworkInformation.NetworkInterface nic = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()[0];

                System.Net.NetworkInformation.GatewayIPAddressInformation gwInfo = nic.GetIPProperties().GatewayAddresses[0];
                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
                string req = "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: " + gwInfo.Address.ToString() + ":1900\r\n" +
                "ST:upnp:rootdevice\r\n" +
                "MAN:\"ssdp:discover\"\r\n" +
                "MX:3\r\n\r\n";
                Socket client = new Socket(AddressFamily.InterNetwork,
                    SocketType.Dgram, ProtocolType.Udp);
                IPEndPoint endPoint = new
                IPEndPoint(IPAddress.Parse(gwInfo.Address.ToString()), 1900);

                client.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReceiveTimeout, 5000);

                byte[] q = Encoding.ASCII.GetBytes(req);
                client.SendTo(q, q.Length, SocketFlags.None, endPoint);
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                EndPoint senderEP = (EndPoint)sender;

                byte[] data = new byte[1024];
                int recv = client.ReceiveFrom(data, ref senderEP);
                string queryResponse = "";
                queryResponse = Encoding.ASCII.GetString(data);

                DateTime start = DateTime.Now;

                string resp = queryResponse;
                if (resp.Contains("upnp:rootdevice"))
                {
                    resp = resp.Substring(resp.ToLower().IndexOf("location:") + 9);
                    resp = resp.Substring(0, resp.IndexOf("\r")).Trim();
                    if (!string.IsNullOrEmpty(ServiceUrl = GetServiceUrl(resp)))
                    {
                        DescUrl = resp;
                        UPnPAvailable = true;
                    }
                }
                UPnPAvailable = false;

                Discovered = true;
                return UPnPAvailable;
            }
        }

        private static string GetServiceUrl(string resp)
        {
            XmlDocument desc = new XmlDocument();
            try
            {
                desc.Load(WebRequest.Create(resp).GetResponse().GetResponseStream());
            }
            catch (Exception)
            {
                return null;
            }
            XmlNamespaceManager nsMgr = new XmlNamespaceManager(desc.NameTable);
            nsMgr.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");
            XmlNode typen = desc.SelectSingleNode("//tns:device/tns:deviceType/text()", nsMgr);
            if (!typen.Value.Contains("InternetGatewayDevice"))
                return null;
            XmlNode node = desc.SelectSingleNode("//tns:service[tns:serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"]/tns:controlURL/text()", nsMgr);
            if (node == null)
                return null;
            XmlNode eventnode = desc.SelectSingleNode("//tns:service[tns:serviceType=\"urn:schemas-upnp-org:service:WANIPConnection:1\"]/tns:eventSubURL/text()", nsMgr);
            EventUrl = CombineUrls(resp, eventnode.Value);
            return CombineUrls(resp, node.Value);
        }

        private static string CombineUrls(string resp, string p)
        {
            int n = resp.IndexOf("://");
            n = resp.IndexOf('/', n + 3);
            return resp.Substring(0, n) + p;
        }

        /// <summary>
        /// Forwards an external port to the same internal port
        /// </summary>
        /// <param name="port">The port to map (both external and internal)</param>
        /// <param name="protocol">The protocol type to map</param>
        /// <param name="description">The description of this mapping</param>
        public static void ForwardPort(int port, ProtocolType protocol, string description)
        {
            if (string.IsNullOrEmpty(ServiceUrl))
            {
                Discover();
                if (string.IsNullOrEmpty(ServiceUrl))
                    throw new Exception("No UPnP service available");
            }

            IPHostEntry ipEntry = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress addr = ipEntry.AddressList[0];

            XmlDocument xdoc = SOAPRequest(ServiceUrl,
                "<m:AddPortMapping xmlns:m=\"urn:schemas-upnp-org:service:WANIPConnection:1\"><NewRemoteHost xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\"></NewRemoteHost><NewExternalPort xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"ui2\">" +
                port.ToString() + "</NewExternalPort><NewProtocol xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\">" +
                protocol.ToString().ToUpper() + "</NewProtocol><NewInternalPort xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"ui2\">" +
                port.ToString() + "</NewInternalPort><NewInternalClient xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\">" +
                addr + "</NewInternalClient><NewEnabled xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"boolean\">1</NewEnabled><NewPortMappingDescription xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"string\">" +
                description + "</NewPortMappingDescription><NewLeaseDuration xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"ui4\">0</NewLeaseDuration></m:AddPortMapping>",
                "AddPortMapping");
        }

        public static void DeleteForwardingRule(int port, ProtocolType protocol)
        {
            if (string.IsNullOrEmpty(ServiceUrl))
            {
                Discover();
                if (string.IsNullOrEmpty(ServiceUrl))
                    throw new Exception("No UPnP service available");
            }

            XmlDocument xdoc = SOAPRequest(ServiceUrl,
            "<u:DeletePortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
            "<NewRemoteHost></NewRemoteHost>" +
            "<NewExternalPort>" + port.ToString() + "</NewExternalPort>" +
            "<NewProtocol>" + protocol.ToString().ToUpper() + "</NewProtocol>" +
            "</u:DeletePortMapping>", "DeletePortMapping");
        }

        public static IPAddress GetExternalIP()
        {
            if (string.IsNullOrEmpty(ServiceUrl))
                throw new Exception("No UPnP service available or Discover() has not been called");
            XmlDocument xdoc = SOAPRequest(ServiceUrl, "<u:GetExternalIPAddress xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" +
            "</u:GetExternalIPAddress>", "GetExternalIPAddress");
            XmlNamespaceManager nsMgr = new XmlNamespaceManager(xdoc.NameTable);
            nsMgr.AddNamespace("tns", "urn:schemas-upnp-org:device-1-0");
            string IP = xdoc.SelectSingleNode("//NewExternalIPAddress/text()", nsMgr).Value;
            return IPAddress.Parse(IP);
        }

        public static XmlDocument SOAPRequest(string url, string soap, string function)
        {
            string req = "<?xml version=\"1.0\"?>" +
            "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            "<s:Body>" +
            soap +
            "</s:Body>" +
            "</s:Envelope>";
            WebRequest r = HttpWebRequest.Create(url);
            r.Timeout = 10000;
            r.Method = "POST";
            byte[] b = Encoding.UTF8.GetBytes(req);
            r.Headers.Add("SOAPACTION", "\"urn:schemas-upnp-org:service:WANIPConnection:1#" + function + "\"");
            r.ContentType = "text/xml; charset=\"utf-8\"";
            r.ContentLength = b.Length;
            r.GetRequestStream().Write(b, 0, b.Length);
            XmlDocument resp = new XmlDocument();
            WebResponse wres = r.GetResponse();
            Stream ress = wres.GetResponseStream();
            resp.Load(ress);
            return resp;
        }
    }
}
