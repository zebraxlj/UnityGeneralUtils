using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Utils.FileReader
{
    /// <summary>
    /// TSV配置表基类，支持从TSV文件读取配置数据并映射到类字段
    /// </summary>
    public abstract class TsvKeyValueTableBase<T> where T : TsvKeyValueTableBase<T>, new()
    {
        // 单例实例
        private static T s_Instance;
        // 线程安全锁
        private static readonly object s_Lock = new object();

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static T Instance
        {
            get
            {
                // 第一次检查，避免不必要的锁定
                if (s_Instance == null)
                {
                    // 添加线程安全锁，确保只创建一个实例
                    lock (s_Lock)
                    {
                        // 第二次检查，确保在锁定期间没有其他线程创建实例
                        if (s_Instance == null)
                        {
                            s_Instance = new T();
                            s_Instance.LoadFromFile();
                        }
                    }
                }
                return s_Instance;
            }
        }

        /// <summary>
        /// TSV文件路径（由子类重写）
        /// </summary>
        protected abstract string FilePath { get; }
        
        /// <summary>
        /// TSV读取配置（可由子类重写）
        /// </summary>
        protected virtual TsvReaderConfig ReaderConfig => new()
        {
            hasHeader = true,
            skipLineCnt = 0,
        };

        /// <summary>
        /// 数据类型映射表（C#类型到TSV数据类型）
        /// </summary>
        private readonly Dictionary<Type, HashSet<string>> m_DataTypeMap = new()
        {
            {typeof(bool), new HashSet<string> {"bool", "boolean"}},
            {typeof(byte), new HashSet<string> {"byte"}},
            {typeof(char), new HashSet<string> {"char"}},
            {typeof(double), new HashSet<string> {"double"}},
            {typeof(float), new HashSet<string> {"float"}},
            {typeof(int), new HashSet<string> {"int", "integer"}},
            {typeof(long), new HashSet<string> {"long"}},
            {typeof(short), new HashSet<string> {"short"}},
            {typeof(string), new HashSet<string> {"str", "string"}},
        };

        /// <summary>
        /// 根据数据类型转换值
        /// </summary>
        /// <param name="value">原始字符串值</param>
        /// <param name="dataType">数据类型字符串</param>
        /// <param name="targetType">目标字段类型</param>
        /// <returns>转换后的值</returns>
        private object ConvertValue(string value, string dataType, Type targetType)
        {
            // 先检查字符串是否为空
            if (string.IsNullOrEmpty(value))
            {
                // 返回目标类型的默认值
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }
            // 检查数据类型 Enum
            if (targetType.IsEnum)
            {
                if (!dataType.Contains("enum", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DataException($"targetType={targetType}, dataType={dataType} not a enum");
                }
                return Enum.Parse(targetType, value, true);
            }
            // 检查其他数据类型
            if (!m_DataTypeMap.TryGetValue(targetType, out HashSet<string> allowedNames))
            {
                throw new DataException($"unsupported targetType={targetType}");
            }
            if (!allowedNames.Contains(dataType))
            {
                throw new DataException($"unsupported conversion: dataType={dataType} -> targetType={targetType}");
            }
            // 预期大部分配置为 string，直接返回更快
            return targetType == typeof(string) ? value : Convert.ChangeType(value, targetType);
        }

        private string TableName => GetType().Name;

        /// <summary>
        /// 从文件加载配置
        /// </summary>
        private void LoadFromFile()
        {
            Debug.Log($"[TsvConfTableBase] <LoadFromFile> table={TableName} path={FilePath}");
            
            if (TsvReader.TryRead(FilePath, ReaderConfig, out string[] header, out List<string[]> rows))
            {
                Debug.Log($"[TsvConfTableBase] <LoadFromFile> table={TableName} Read {rows.Count} rows");
                MapDataToFields(header, rows);
            }
            else
            {
                Debug.LogWarning($"[TsvConfTableBase] <LoadFromFile> table={TableName} Failed to read");
            }
        }

        /// <summary>
        /// 将TSV数据映射到类字段
        /// </summary>
        /// <param name="header">表头</param>
        /// <param name="rows">数据行</param>
        private void MapDataToFields(string[] header, List<string[]> rows)
        {
            // 获取表头索引
            int keyIndex = -1, dataTypeIndex = -1, valueIndex = -1;
            for (int i = 0; i < header.Length; i++)
            {
                string headerName = header[i].Trim().ToLower();
                if (headerName == "key") keyIndex = i;
                else if (headerName == "datatype") dataTypeIndex = i;
                else if (headerName == "value") valueIndex = i;
            }

            if (keyIndex == -1 || dataTypeIndex == -1 || valueIndex == -1)
            {
                Debug.LogError($"[TsvConfTableBase] <MapDataToFields> table={TableName} Missing required headers: Key, DataType, Value");
                return;
            }

            // 获取所有公共字段，并创建字段名字典映射
            Dictionary<string, FieldInfo> modelFieldMap = new(StringComparer.OrdinalIgnoreCase);
            foreach (FieldInfo field in GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                modelFieldMap[field.Name] = field;
            }

            // 用于跟踪哪些字段已经从TSV文件中获取到了值
            HashSet<string> populatedFields = new(StringComparer.OrdinalIgnoreCase);
            int maxColumnIndex = Math.Max(keyIndex, Math.Max(dataTypeIndex, valueIndex));

            // 遍历每一行数据
            foreach (string[] row in rows)
            {
                if (row.Length <= maxColumnIndex)
                    continue;

                string key = row[keyIndex].Trim();
                string dataType = row[dataTypeIndex].Trim().ToLower();
                string valueStr = row[valueIndex].Trim();

                // 查找匹配的字段
                if (!modelFieldMap.TryGetValue(key, out FieldInfo field))
                {
                    Debug.LogError($"[TsvConfTableBase] <MapDataToFields> table={TableName} Field '{key}' not found.");
                    continue;
                }

                // 转换值并设置到字段
                try
                {
                    object value = ConvertValue(valueStr, dataType, field.FieldType);
                    field.SetValue(this, value);
                    Debug.Log($"[TsvConfTableBase] <MapDataToFields> table={TableName} Set {field.Name}={value} ({dataType})");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TsvConfTableBase] <MapDataToFields> table={TableName} Failed to convert {valueStr} to type={dataType} for field={key}: {e.Message}");
                }
                // 记录 TSV 有值的字段
                populatedFields.Add(field.Name);
            }

            // 检查类中哪些字段在TSV文件中不存在
            foreach (FieldInfo field in modelFieldMap.Values.Where(field => !populatedFields.Contains(field.Name)))
            {
                Debug.LogError($"[TsvConfTableBase] <MapDataToFields> table={TableName} Missing field='{field.Name}' in TSV file");
            }
        }
    }
}