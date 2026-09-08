using System;
using System.IO;

namespace LianDian.Core
{
    /// <summary>相对目录解析：向上级目录树查找已存在目录（适配 bin\Debug\net472 深目录），缺失时回退基目录。</summary>
    public static class PathResolver
    {
        public static string Resolve(string baseDir, string relative, bool createIfMissing = false)
        {
            if (string.IsNullOrEmpty(relative)) return baseDir;
            if (Path.IsPathRooted(relative)) return relative;

            DirectoryInfo current = string.IsNullOrEmpty(baseDir) ? null : new DirectoryInfo(baseDir);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, relative);
                if (Directory.Exists(candidate)) return candidate;
                current = current.Parent;
            }

            string fallback = Path.Combine(baseDir ?? AppDomain.CurrentDomain.BaseDirectory, relative);
            if (createIfMissing) Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
