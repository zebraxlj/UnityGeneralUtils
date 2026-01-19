using UnityEngine;

public class Starter : MonoBehaviour
{
    void Start()
    {
        Debug.Log("[Starter] <Start>");
        ResumableDownloaderDemo.RunDownloadManager().Forget();
        TsvReaderDemo.ReadTempConfigFile();
        TsvKeyValueTableDemo.AppInfoDemo();
        TsvKeyValueTableDemo.AppInfoDemo();
        TsvKeyValueTableDemo.SampleKeyValueTableDemo();
        TsvKeyValueTableDemo.TempConfigDemo();
    }
}
