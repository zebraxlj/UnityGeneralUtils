using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Utils.FileReader
{
    public class TsvReaderConfig
    {
        public bool hasHeader = true;
        public int skipLineCnt = 0;
    }

    /// <summary>
    /// TSV 文件读取器，严格模式
    /// 详细规范见 https://www.loc.gov/preservation/digital/formats/fdd/fdd000533.shtml
    ///          https://www.iana.org/assignments/media-types/text/tab-separated-values
    /// 1. 每一行的列数必须与 header 中的列数一致
    /// 2. 不支持转义字符
    /// </summary>
    public static class TsvReader
    {
        private static string[] ParseLine(string line)
        {
            return line.Split('\t');
        }

        /// <summary>
        /// 尝试读取 TSV 文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="config">配置</param>
        /// <param name="data">数据</param>
        /// <param name="header"></param>
        /// <returns>是否成功</returns>
        public static bool TryRead(string filePath, TsvReaderConfig config, out string[] header, out List<string[]> data)
        {
            data = new List<string[]>();
            header = new string[0];

            if (!File.Exists(filePath))
            {
                Debug.Log($"[TsvReader] <Read> file not exists filePath={filePath}");
                return false;
            }

            Debug.Log($"[TsvReader] <Read> filePath={filePath}");
            string fileName = Path.GetFileName(filePath);

            using StreamReader reader = new(filePath);
            for (int i = 0; i < config.skipLineCnt; i++)
            {
                if (reader.EndOfStream)
                {
                    Debug.LogError($"[TsvReader] <Read> fileName={fileName} Bad skipLineCnt={config.skipLineCnt} lineInFile={i + 1}");
                    return false;
                }
                reader.ReadLine();
            }

            // Read header
            int columnCount;
            if (config.hasHeader)
            {
                if (reader.EndOfStream)
                {
                    Debug.LogError($"[TsvReader] <Read> fileName={fileName} No header. skipLineCnt={config.skipLineCnt}");
                    return false;
                }
                header = ParseLine(reader.ReadLine());
                columnCount = header.Length;
            }
            else
            {
                // No header row, so we'll use the first row as data
                string[] firstRow = ParseLine(reader.ReadLine());
                data.Add(firstRow);
                columnCount = firstRow.Length;
                header = new string[columnCount];
                for (int i = 0; i < columnCount; i++)
                {
                    header[i] = $"Column{i + 1}";
                }
            }

            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine();

                string[] row = ParseLine(line);
                if (row.Length != columnCount)
                {
                    Debug.LogError($"[TsvReader] <Read> line={line} columnCount={columnCount}");
                    // 不跳过，直接返回，避免错误修改老的行，导致老的行读取失败被跳过
                    return false;
                }
                data.Add(row);
            }

            return true;
        }
    }
}
