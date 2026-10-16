using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Minimal MSIX writer (no Windows SDK needed): packs a folder that already contains AppxManifest.xml into an
/// uncompressed ("stored") ZIP64 package with AppxBlockMap.xml (SHA-256 per 64 KB block) and [Content_Types].xml,
/// the same layout MakeAppx produces with /nc. The result is unsigned: the Microsoft Store signs packages on submission;
/// to side-load for testing, sign it with SignTool and a trusted certificate.
/// Pure .NET (no UnityEditor) so it can be tested outside Unity.
/// </summary>
public static class MsixPacker
{
    private const int BlockSize = 65536;

    private class Entry
    {
        public string zipName, blockName;
        public long size, offset;
        public uint crc;
        public int lfhSize;
        public List<string> hashes = new List<string>();
    }

    public static void Pack(string folder, string outFile)
    {
        folder = Path.GetFullPath(folder);
        if (!File.Exists(Path.Combine(folder, "AppxManifest.xml"))) throw new FileNotFoundException("AppxManifest.xml missing in " + folder);
        var files = new List<string>();
        Collect(folder, folder, files);
        // Payload first, AppxManifest.xml last of the payload (like MakeAppx), then block map and content types.
        files.Remove("AppxManifest.xml");
        files.Sort(StringComparer.Ordinal);
        files.Add("AppxManifest.xml");

        var entries = new List<Entry>();
        var extensions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var overrides = new List<string>();
        using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            foreach (var rel in files)
            {
                var e = WriteFile(w, Path.Combine(folder, rel.Replace('/', Path.DirectorySeparatorChar)), rel, true);
                entries.Add(e);
                if (rel == "AppxManifest.xml") continue;
                string ext = Path.GetExtension(rel).TrimStart('.').ToLowerInvariant();
                if (ext.Length == 0 || ext.IndexOf(' ') >= 0) overrides.Add(e.zipName);
                else if (!extensions.ContainsKey(ext)) extensions[ext] = ContentType(ext);
            }
            byte[] blockMap = Encoding.UTF8.GetBytes(BlockMapXml(entries));
            var bm = WriteBytes(w, "AppxBlockMap.xml", blockMap);
            byte[] types = Encoding.UTF8.GetBytes(ContentTypesXml(extensions, overrides));
            var ct = WriteBytes(w, "[Content_Types].xml", types);
            var all = new List<Entry>(entries) { bm, ct };
            WriteCentralDirectory(w, all);
        }
    }

    private static void Collect(string root, string dir, List<string> files)
    {
        foreach (var f in Directory.GetFiles(dir))
        {
            string rel = f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            string n = Path.GetFileName(f);
            if (n == "AppxBlockMap.xml" || n == "[Content_Types].xml" || n == "AppxSignature.p7x") continue;
            files.Add(rel);
        }
        foreach (var d in Directory.GetDirectories(dir)) Collect(root, d, files);
    }

    // ------------------------------------------------------------------ zip writing (stored, ZIP64)

    private static Entry WriteFile(BinaryWriter w, string path, string rel, bool hashBlocks)
    {
        var e = new Entry { zipName = EncodeName(rel), blockName = rel.Replace('/', '\\'), offset = w.BaseStream.Position };
        var info = new FileInfo(path);
        e.size = info.Length;
        // CRC + block hashes in a first pass (files can be large: stream them).
        uint crc = 0xFFFFFFFF;
        var buf = new byte[BlockSize];
        using (var sha = SHA256.Create())
        using (var src = File.OpenRead(path))
        {
            int read;
            while ((read = ReadFull(src, buf)) > 0)
            {
                crc = Crc(crc, buf, read);
                if (hashBlocks) e.hashes.Add(Convert.ToBase64String(sha.ComputeHash(buf, 0, read)));
            }
        }
        e.crc = crc ^ 0xFFFFFFFF;
        WriteLocalHeader(w, e);
        using (var src = File.OpenRead(path)) src.CopyTo(w.BaseStream);
        return e;
    }

    private static Entry WriteBytes(BinaryWriter w, string name, byte[] data)
    {
        var e = new Entry { zipName = name, blockName = name, offset = w.BaseStream.Position, size = data.Length };
        e.crc = Crc(0xFFFFFFFF, data, data.Length) ^ 0xFFFFFFFF;
        WriteLocalHeader(w, e);
        w.Write(data);
        return e;
    }

    private static int ReadFull(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int r = s.Read(buf, total, buf.Length - total);
            if (r <= 0) break;
            total += r;
        }
        return total;
    }

    private static void WriteLocalHeader(BinaryWriter w, Entry e)
    {
        byte[] name = Encoding.UTF8.GetBytes(e.zipName);
        w.Write(0x04034b50u);
        w.Write((ushort)45);            // version needed: ZIP64
        w.Write((ushort)0);             // flags
        w.Write((ushort)0);             // stored
        w.Write((ushort)0); w.Write((ushort)0x21);   // DOS time/date (1980-01-01)
        w.Write(e.crc);
        w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);  // sizes in the ZIP64 extra field
        w.Write((ushort)name.Length);
        w.Write((ushort)20);
        w.Write(name);
        w.Write((ushort)1); w.Write((ushort)16);
        w.Write(e.size); w.Write(e.size);
        e.lfhSize = 30 + name.Length + 20;
    }

    private static void WriteCentralDirectory(BinaryWriter w, List<Entry> all)
    {
        long cdStart = w.BaseStream.Position;
        foreach (var e in all)
        {
            byte[] name = Encoding.UTF8.GetBytes(e.zipName);
            w.Write(0x02014b50u);
            w.Write((ushort)45); w.Write((ushort)45);
            w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0x21);
            w.Write(e.crc);
            w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
            w.Write((ushort)name.Length);
            w.Write((ushort)28);          // ZIP64 extra: 4 + 24
            w.Write((ushort)0);           // comment
            w.Write((ushort)0);           // disk
            w.Write((ushort)0);           // internal attrs
            w.Write(0u);                  // external attrs
            w.Write(0xFFFFFFFFu);         // offset in ZIP64 extra
            w.Write(name);
            w.Write((ushort)1); w.Write((ushort)24);
            w.Write(e.size); w.Write(e.size); w.Write(e.offset);
        }
        long cdEnd = w.BaseStream.Position;
        long cdSize = cdEnd - cdStart;
        // ZIP64 end of central directory record + locator
        w.Write(0x06064b50u);
        w.Write((ulong)44);
        w.Write((ushort)45); w.Write((ushort)45);
        w.Write(0u); w.Write(0u);
        w.Write((ulong)all.Count); w.Write((ulong)all.Count);
        w.Write((ulong)cdSize); w.Write((ulong)cdStart);
        w.Write(0x07064b50u);
        w.Write(0u);
        w.Write((ulong)cdEnd);
        w.Write(1u);
        // classic end of central directory (all fields deferred to ZIP64)
        w.Write(0x06054b50u);
        w.Write((ushort)0xFFFF); w.Write((ushort)0xFFFF);
        w.Write((ushort)0xFFFF); w.Write((ushort)0xFFFF);
        w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
        w.Write((ushort)0);
    }

    // ------------------------------------------------------------------ XML parts

    private static string BlockMapXml(List<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        sb.Append("<BlockMap xmlns=\"http://schemas.microsoft.com/appx/2010/blockmap\" HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");
        foreach (var e in entries)
        {
            sb.Append("<File Name=\"").Append(Xml(e.blockName)).Append("\" Size=\"").Append(e.size).Append("\" LfhSize=\"").Append(e.lfhSize).Append("\"");
            if (e.hashes.Count == 0) { sb.Append("/>"); continue; }
            sb.Append('>');
            foreach (var h in e.hashes) sb.Append("<Block Hash=\"").Append(h).Append("\"/>");
            sb.Append("</File>");
        }
        sb.Append("</BlockMap>");
        return sb.ToString();
    }

    private static string ContentTypesXml(SortedDictionary<string, string> ext, List<string> overrides)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        foreach (var kv in ext) sb.Append("<Default Extension=\"").Append(Xml(kv.Key)).Append("\" ContentType=\"").Append(kv.Value).Append("\"/>");
        foreach (var o in overrides) sb.Append("<Override PartName=\"/").Append(Xml(o)).Append("\" ContentType=\"application/octet-stream\"/>");
        sb.Append("<Override PartName=\"/AppxManifest.xml\" ContentType=\"application/vnd.ms-appx.manifest+xml\"/>");
        sb.Append("<Override PartName=\"/AppxBlockMap.xml\" ContentType=\"application/vnd.ms-appx.blockmap+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string ContentType(string ext)
    {
        switch (ext)
        {
            case "exe": case "dll": return "application/x-msdownload";
            case "png": return "image/png";
            case "jpg": case "jpeg": return "image/jpeg";
            case "xml": case "config": return "text/xml";
            case "json": return "application/json";
            case "txt": case "md": case "ini": return "text/plain";
            default: return "application/octet-stream";
        }
    }

    /// <summary>ZIP part name: forward slashes, URI-escaped (spaces, brackets, non-ASCII), like the OPC rules.</summary>
    private static string EncodeName(string rel)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(rel))
        {
            char c = (char)b;
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || "-._~/!$&'()*+,;=:@".IndexOf(c) >= 0;
            if (ok && b < 128) sb.Append(c); else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ------------------------------------------------------------------ CRC-32

    private static uint[] table;
    private static uint Crc(uint crc, byte[] data, int len)
    {
        if (table == null)
        {
            table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
        }
        for (int i = 0; i < len; i++) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
