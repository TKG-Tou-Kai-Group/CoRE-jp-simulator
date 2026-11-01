using System;
using System.Net;
using System.Net.Sockets;

namespace CoreSim.Net
{
    /// <summary>参加先アドレスの解釈。"host:port" (Secure Share Net の公開アドレスなど) を受け付ける。</summary>
    public static class NetAddress
    {
        /// <summary>"host:port" / "[v6]:port" を分ける。ポートが無ければ port はそのまま。</summary>
        public static void SplitHostPort(ref string host, ref int port)
        {
            if (string.IsNullOrEmpty(host)) return;
            host = host.Trim();
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int close = host.IndexOf(']');
                if (close < 0) return;
                string rest = host.Substring(close + 1);
                host = host.Substring(1, close - 1);
                if (rest.StartsWith(":", StringComparison.Ordinal) && TryPort(rest.Substring(1), out int p6)) port = p6;
                return;
            }
            int first = host.IndexOf(':');
            if (first < 0 || first != host.LastIndexOf(':')) return; // ':' なし、または裸の IPv6
            if (TryPort(host.Substring(first + 1), out int p))
            {
                port = p;
                host = host.Substring(0, first);
            }
        }

        private static bool TryPort(string text, out int port)
        {
            return int.TryParse(text, out port) && port > 0 && port <= 65535;
        }

        /// <summary>IP リテラルまたはホスト名を解決する。IPv4 を優先 (UDP ソケットが IPv4 のため)。</summary>
        public static IPAddress ResolveHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            if (IPAddress.TryParse(host, out IPAddress ip)) return ip;
            try
            {
                IPAddress[] addrs = Dns.GetHostAddresses(host);
                foreach (IPAddress a in addrs)
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork) return a;
                }
                return addrs.Length > 0 ? addrs[0] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
