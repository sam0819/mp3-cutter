// MP3 剪接器桌面版：把內嵌的 index.html 用本機 HTTP 提供，再以 Edge／Chrome 的 App 模式開成獨立視窗。
// 視窗全部關閉後程式自動結束。只監聽 127.0.0.1，外部連不進來。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("MP3 剪接器")]
[assembly: AssemblyProduct("MP3 剪接器")]
[assembly: AssemblyDescription("剪接 MP3 與 M4A，匯出 MP3")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    const int PreferredPort = 47815;
    const string PingReply = "mp3cutter";
    static byte[] page;

    [STAThread]
    static void Main()
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("index.html"))
        using (MemoryStream ms = new MemoryStream())
        {
            s.CopyTo(ms);
            page = ms.ToArray();
        }

        TcpListener listener = null;
        int port = PreferredPort;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, PreferredPort);
            listener.Start();
        }
        catch (SocketException)
        {
            listener = null;
            if (!AlreadyRunning(PreferredPort))
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
        }

        if (listener != null)
        {
            SetHandleInformation(listener.Server.Handle, 1, 0);   // HANDLE_FLAG_INHERIT 關掉
            Thread t = new Thread(delegate () { Serve(listener); });
            t.IsBackground = true;
            t.Start();
        }

        string url = "http://127.0.0.1:" + port + "/";
        string browser = FindBrowser();

        if (browser == null) Process.Start(url);
        else
        {
            string profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mp3Cutter", "browser-" + port);
            ProcessStartInfo psi = new ProcessStartInfo(browser,
                "--app=" + url + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --window-size=1200,900");
            psi.UseShellExecute = true;   // 不讓瀏覽器繼承監聽中的 socket，否則程式結束後埠仍被佔住
            Process.Start(psi);
        }
        // 已有一份在執行時，新視窗由原本那份的伺服器提供，這份直接結束
        if (listener == null) return;
        // 瀏覽器關掉視窗後常留在背景，所以改看網頁的心跳：所有視窗都關了就結束
        while (!ShouldExit()) Thread.Sleep(1000);
    }

    static readonly object gate = new object();
    static readonly Dictionary<string, DateTime> windows = new Dictionary<string, DateTime>();
    static readonly DateTime started = DateTime.UtcNow;
    static DateTime emptySince = DateTime.UtcNow;
    static bool everSeen;

    static void Touch(string id, bool bye)
    {
        lock (gate)
        {
            if (bye)
            {
                if (windows.Remove(id) && windows.Count == 0) emptySince = DateTime.UtcNow;
            }
            else
            {
                windows[id] = DateTime.UtcNow;
                everSeen = true;
            }
        }
    }

    static bool ShouldExit()
    {
        lock (gate)
        {
            DateTime now = DateTime.UtcNow;
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, DateTime> kv in windows)
                if ((now - kv.Value).TotalSeconds > 90) stale.Add(kv.Key);   // 視窗縮小時瀏覽器會放慢計時器，給寬一點
            foreach (string k in stale) windows.Remove(k);
            if (stale.Count > 0 && windows.Count == 0) emptySince = now;
            if (!everSeen) return (now - started).TotalSeconds > 60;
            return windows.Count == 0 && (now - emptySince).TotalSeconds > 5;
        }
    }

    static string QueryId(string query)
    {
        foreach (string pair in query.Split('&'))
            if (pair.StartsWith("id=")) return Uri.UnescapeDataString(pair.Substring(3));
        return "?";
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

    static bool AlreadyRunning(int port)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/ping");
            req.Timeout = 2000;
            req.ReadWriteTimeout = 2000;
            req.Proxy = null;
            using (WebResponse res = req.GetResponse())
            using (StreamReader r = new StreamReader(res.GetResponseStream()))
                return r.ReadToEnd() == PingReply;
        }
        catch (Exception) { return false; }
    }

    static string FindBrowser()
    {
        string[] bases = {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        string[] rel = { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" };
        foreach (string r in rel)
            foreach (string b in bases)
            {
                if (string.IsNullOrEmpty(b)) continue;
                string path = Path.Combine(b, r);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    static void Serve(TcpListener listener)
    {
        while (true)
        {
            TcpClient c;
            try { c = listener.AcceptTcpClient(); }
            catch (Exception) { return; }
            ThreadPool.QueueUserWorkItem(delegate (object o) { Handle((TcpClient)o); }, c);
        }
    }

    static void Handle(TcpClient client)
    {
        try
        {
            using (client)
            {
                NetworkStream ns = client.GetStream();
                StreamReader reader = new StreamReader(ns, Encoding.ASCII);
                string line = reader.ReadLine();
                if (line == null) return;
                string h;
                while (!string.IsNullOrEmpty(h = reader.ReadLine())) { }
                string[] parts = line.Split(' ');
                string path = parts.Length > 1 ? parts[1] : "/";
                int q = path.IndexOf('?');
                string query = q >= 0 ? path.Substring(q + 1) : "";
                if (q >= 0) path = path.Substring(0, q);

                int code = 200;
                string type = "text/html; charset=utf-8";
                byte[] body = page;
                if (path == "/ping") { body = Encoding.ASCII.GetBytes(PingReply); type = "text/plain"; }
                else if (path == "/alive" || path == "/bye") { Touch(QueryId(query), path == "/bye"); body = new byte[0]; type = "text/plain"; }
                else if (path != "/" && path != "/index.html") { code = 404; body = Encoding.ASCII.GetBytes("Not found"); type = "text/plain"; }

                byte[] head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 " + code + (code == 200 ? " OK" : " Not Found") + "\r\n" +
                    "Content-Type: " + type + "\r\n" +
                    "Content-Length: " + body.Length + "\r\n" +
                    "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
                ns.Write(head, 0, head.Length);
                ns.Write(body, 0, body.Length);
            }
        }
        catch (Exception) { }
    }
}
