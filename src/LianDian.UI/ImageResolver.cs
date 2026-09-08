using System;
using System.Drawing;
using System.IO;
using LianDian.Core;

namespace LianDian.UI
{
    /// <summary>产品图/指导书目录解析与文件名匹配（完全同名忽略大小写优先、包含模糊匹配兜底）。</summary>
    public static class ImageResolver
    {
        public static string ResolveDir(string relative)
        {
            return PathResolver.Resolve(AppDomain.CurrentDomain.BaseDirectory, relative);
        }

        public static string FindProductImage(string dir, string productName)
        {
            if (string.IsNullOrEmpty(productName) || !Directory.Exists(dir)) return null;
            foreach (string file in Directory.GetFiles(dir))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file), productName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            foreach (string file in Directory.GetFiles(dir))
            {
                if (Path.GetFileNameWithoutExtension(file).IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return file;
            }
            return null;
        }

        public static Image LoadSafely(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var source = Image.FromStream(fs))
                    return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }
    }
}
