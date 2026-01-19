using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Utils.FileReader;


# region TsvKeyValueTableDemo

/// <summary>
/// 应用信息配置类
/// </summary>
public class ConfAppInfo : TsvKeyValueTableBase<ConfAppInfo>
{
    protected override string FilePath => Path.Combine(Application.persistentDataPath, "FileReader", "AppInfo.tsv");

    // 配置字段
    public string AppName;
    public int Version;
    public float Scale;
    public bool IsDebug;
    public double Pi;
}

/// <summary>
/// 示例配置类
/// </summary>
public class ConfTempConfig : TsvKeyValueTableBase<ConfTempConfig>
{
    protected override string FilePath => Path.Combine(Application.persistentDataPath, "FileReader", "TempConfig.tsv");

    protected override TsvReaderConfig ReaderConfig => new()
    {
        hasHeader = true,
        skipLineCnt = 1,
    };

    // 配置字段
    public string UserAgent;
    public string SecChUa;
    public string SecChUaPlatform;
    public string TestMissingInTsv;
}

public enum TestEnum
{
    TestEnumValue,
    TestEnumValue2,
    Unknown,
}

public class ConfSampleKeyValueTable : TsvKeyValueTableBase<ConfSampleKeyValueTable>
{
    protected override string FilePath =>
        Path.Combine(Application.persistentDataPath, "FileReader", "SampleKeyValueTable.tsv");

    protected override TsvReaderConfig ReaderConfig => new()
    {
        skipLineCnt = 1,
    };

    // 配置字段
    public bool FieldBool;
    public bool FieldBoolean;
    public byte FieldByte;
    public char FieldChar;
    public double FieldDouble;
    public TestEnum FieldEnum = TestEnum.Unknown;
    public float FieldFloat;
    public int FieldInt;
    public long FieldLong;
    public short FieldShort = -1;
    public string FieldStr;
    public string FieldString;
    public string TestMissingInTsv;
}

/// <summary>
/// TSV配置表演示类
/// </summary>
public static class TsvKeyValueTableDemo
{
    public static void AppInfoDemo()
    {
        Debug.Log($"[TsvKeyValueTableDemo] <AppInfoDemo> AppInfoDemo Start");
        ConfAppInfo config = ConfAppInfo.Instance;
        Debug.Log($"[TsvKeyValueTableDemo] <AppInfoDemo> AppName: {config.AppName}");
        Debug.Log($"[TsvKeyValueTableDemo] <AppInfoDemo> Version: {config.Version}");
        Debug.Log($"[TsvKeyValueTableDemo] <AppInfoDemo> Scale: {config.Scale}");
        Debug.Log($"[TsvKeyValueTableDemo] <AppInfoDemo> IsDebug: {config.IsDebug}");
        Debug.Log($"[TsvKeyValueTableDemo] <AppInfoDemo> Pi: {config.Pi}");
    }

    public static void TempConfigDemo()
    {
        Debug.Log($"[TsvKeyValueTableDemo] <ConfTempConfig> TempConfigDemo Start");
        ConfTempConfig config = ConfTempConfig.Instance;
        Debug.Log($"[TsvKeyValueTableDemo] <ConfTempConfig> UserAgent: {config.UserAgent}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfTempConfig> SecChUa: {config.SecChUa}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfTempConfig> SecChUaPlatform: {config.SecChUaPlatform}");
    }

    public static void SampleKeyValueTableDemo()
    {
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> SampleKeyValueTableDemo Start");
        ConfSampleKeyValueTable config = ConfSampleKeyValueTable.Instance;
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldBool: {config.FieldBool}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldBoolean: {config.FieldBoolean}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldByte: {config.FieldByte}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldChar: {config.FieldChar}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldDouble: {config.FieldDouble}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldEnum: {config.FieldEnum}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldFloat: {config.FieldFloat}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldInt: {config.FieldInt}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldLong: {config.FieldLong}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldShort: {config.FieldShort}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldStr: {config.FieldStr}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> FieldString: {config.FieldString}");
        Debug.Log($"[TsvKeyValueTableDemo] <ConfSampleKeyValueTable> TestMissingInTsv: {config.TestMissingInTsv}");
    }
}

# endregion

#region TsvReaderDemo

public static class TsvReaderDemo
{
    private static string sDataPath = Path.Combine(Application.persistentDataPath, "FileReader");
    public static void ReadSampleFile()
    {
        string filePath = Path.Combine(sDataPath, "mtcars.tsv");
        if (TsvReader.TryRead(filePath, new TsvReaderConfig{hasHeader=false}, out string[] header, out List<string[]> rows))
        {
            Debug.Log($"[TsvReaderDemo] <ReadSampleFile> Success. ColCnt={header.Length} RowCnt={rows.Count}");
            Debug.Log($"[TsvReaderDemo] <ReadSampleFile> header = {string.Join('\t', header)}");
            foreach (string[] row in rows)
            {
                Debug.Log($"[TsvReaderDemo] <ReadSampleFile> row = {string.Join('\t', row)}");
            }
        }
        else
        {
            Debug.Log("[TsvReaderDemo] <ReadSampleFile> Fail.");
        }
    }

    public static void ReadTempConfigFile()
    {
        string filePath = Path.Combine(sDataPath, "TempConfig.tsv");
        if (TsvReader.TryRead(filePath, new TsvReaderConfig(), out string[] header, out List<string[]> rows))
        {
            Debug.Log($"[TsvReaderDemo] <ReadTempConfigFile> Success. ColCnt={header.Length} RowCnt={rows.Count}");
            Debug.Log($"[TsvReaderDemo] <ReadTempConfigFile> header = {string.Join('\t', header)}");
            foreach (string[] row in rows)
            {
                Debug.Log($"[TsvReaderDemo] <ReadTempConfigFile> row = {string.Join('\t', row)}");
            }
        }
        else
        {
            Debug.Log("[TsvReaderDemo] <ReadTempConfigFile> Fail.");
        }
    }
}

#endregion
