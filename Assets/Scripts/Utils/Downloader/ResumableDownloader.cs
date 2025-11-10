using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Networking;
using Utils.Downloader;

namespace Utils.Downloader
{
    public enum ResumableDownloadTaskStatus
    {
        Unknown,

        Added,
        Cancelled,
        Downloading,
        Downloaded,
        DownloadedPartial,  // 没下完，需要续传
        Failed,
    }

    public class ResumableDownloadManager
    {
        [CanBeNull] public Action<string, ulong, ulong, ResumableDownloadTaskStatus> onProgress;

        private readonly ConcurrentDictionary<string, ResumableDownloadWorker> m_PathToDownloadWorker = new();
        private readonly ConcurrentDictionary<string, ulong> m_BytesReceivedMap = new();
        private readonly ConcurrentDictionary<string, ulong> m_BytesTotalMap = new();
        private readonly CancellationTokenSource m_CancellationTokenSource = new();

        // 用于输出下载进度
        private readonly ConcurrentDictionary<string, float> m_LastLogProgressLogTimeMap = new();
        private float m_LastLogProgressLogTime;
        private const float kProgressLogInterval = 1f;

        private void OnProgress(string path, ulong bytesReceived, ulong bytesTotal, ResumableDownloadTaskStatus status)
        {
            onProgress?.Invoke(path, bytesReceived, bytesTotal, status);

            if (m_CancellationTokenSource.Token.IsCancellationRequested)
                return;

            m_BytesReceivedMap[path] = bytesReceived;
            m_BytesTotalMap[path] = bytesTotal;

            float now = Time.realtimeSinceStartup;
            // 全局日志节流：所有任务共享一个日志输出间隔，避免多任务同时输出进度导致日志过多
            bool isInLogInterval = m_LastLogProgressLogTimeMap.ContainsKey(path) && now - m_LastLogProgressLogTimeMap[path] < kProgressLogInterval;

            if (!m_PathToDownloadWorker.ContainsKey(path))
            {
                Debug.LogError($"[ResumableDownloadManager] <OnProgress> no worker for path={path}");
                return;
            }

            bool isComplete = false;
            switch (status)
            {
                case ResumableDownloadTaskStatus.Cancelled:
                case ResumableDownloadTaskStatus.Downloaded:
                case ResumableDownloadTaskStatus.Failed:
                    isComplete = true;
                    break;
            }

            if (isInLogInterval && !isComplete)
            {
                return;
            }

            m_LastLogProgressLogTimeMap[path] = now;
            ulong byteTotalSum = 0;
            ulong byteReceivedSum = 0;
            bool isLogBytes = false;
            foreach (ulong vTotal in m_BytesTotalMap.Values)
            {
                if (vTotal > 0) continue;
                isLogBytes = true;
                break;
            }

            if (isLogBytes)
            {
                foreach (ulong vReceive in m_BytesReceivedMap.Values)
                {
                    byteReceivedSum += vReceive;
                }
                Debug.Log($"[ResumableDownloadManager] <OnProgress> {status} {byteReceivedSum} bytes");
            }
            else
            {
                foreach ((string k, ulong vTotal) in m_BytesTotalMap)
                {
                    byteTotalSum += vTotal;
                    if (m_BytesReceivedMap.TryGetValue(k, out ulong vReceived))
                    {
                        byteReceivedSum += vReceived;
                    }
                }
                Debug.Log($"[ResumableDownloadManager] <OnProgress> {status} {byteReceivedSum}/{byteTotalSum} bytes");
            }
        }

        public void AddDownloadTask(string url, string path, int timeout = 0)
        {
            m_PathToDownloadWorker[path] = new ResumableDownloadWorker
            {
                path = path,
                url = url,
                timeout = timeout,
                onProgress = OnProgress,
                cancellationToken = m_CancellationTokenSource.Token,
                status = ResumableDownloadTaskStatus.Added,
            };
        }

        public void CancelAll()
        {
            m_CancellationTokenSource?.Cancel();
        }

        public void Dispose()
        {
            CancelAll();
            m_CancellationTokenSource?.Dispose();
        }

        public async UniTask DownloadAll()
        {
            List<string> taskPaths = new();
            List<UniTask<bool>> tasks = new();
            Dictionary<string, UniTask<bool>> pathToTask = new();
            foreach ((string path, ResumableDownloadWorker worker) in m_PathToDownloadWorker)
            {
                taskPaths.Add(path);
                tasks.Add(worker.StartAsync(3));
            }

            bool[] taskResults = await UniTask.WhenAll(tasks);

            for (int i = 0; i < taskResults.Length; i++)
            {
                string path = taskPaths[i];
                Debug.Log($"[ResumableDownloadManager] <DownloadAll> path={path} {m_PathToDownloadWorker[path].status}");
            }
        }
    }

    public class ResumableDownloadWorker
    {
        public string path;
        public string url;
        public int timeout;
        public CancellationToken cancellationToken;
        public Action<string, ulong, ulong, ResumableDownloadTaskStatus> onProgress;

        public ResumableDownloadTaskStatus status = ResumableDownloadTaskStatus.Unknown;

        private static readonly HashSet<int> kRespCodeReqTimeout = new()
        {
            0,  // 没有收到返回，等其他不建议采纳收到的 partial 数据的情况
            200,  // 首次连接，收到部分内容后超时
            206,  // 续传下载，收到部分内容后超时
        };
        private static readonly HashSet<ResumableDownloadTaskStatus> sRetryStatus = new()
        {
            ResumableDownloadTaskStatus.DownloadedPartial,
        };
        
        public async UniTask<bool> StartAsync(int maxAttempt = 1)
        {
            for (int i = 0; i < maxAttempt; i++)
            {
                bool attemptResult = await StartOnce();
                if (!attemptResult && sRetryStatus.Contains(status)) continue;
                return attemptResult;
            }
            return false;
        }

        public async UniTask<bool> StartOnce()
        {
            if (cancellationToken.IsCancellationRequested || status == ResumableDownloadTaskStatus.Cancelled)
            {
                Debug.Log($"[ResumableDownloadWorker] <StartAsync> cancelled path={path}");
                return false;
            }

            long existingLength = 0;
            if (File.Exists(path))
            {
                existingLength = new FileInfo(path).Length;
                Debug.Log($"[ResumableDownloadWorker] <StartAsync> resume from {existingLength} bytes path={path}");
            }
            else
            {
                Debug.Log($"[ResumableDownloadWorker] <StartAsync> start {path}");
            }

            using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbGET);
            if (existingLength > 0)
            {
                request.SetRequestHeader("Range", $"bytes={existingLength}-");
                Debug.Log($"[ResumableDownloadWorker] <StartAsync> Range={request.GetRequestHeader("Range")}");
            }

            ResumableDownloadHandler handler = new(path, onProgress);
            request.downloadHandler = handler;
            request.timeout = timeout;
            status = ResumableDownloadTaskStatus.Downloading;
            try
            {
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                using (cancellationToken.Register(() =>
                {
                    op.webRequest?.Abort();
                    status = ResumableDownloadTaskStatus.Cancelled;
                }))
                {
                    await op;
                }

                if (cancellationToken.IsCancellationRequested || status == ResumableDownloadTaskStatus.Cancelled)
                {
                    Debug.Log($"[ResumableDownloadWorker] <StartAsync> cancelled path={path}");
                    // 取消下载，不写入文件
                    return false;
                }

                if (request.result == UnityWebRequest.Result.Success || request.responseCode == 206)
                {
                    Debug.Log($"[ResumableDownloadWorker] <StartAsync> downloaded code={request.responseCode} path={path}");
                    status = request.responseCode == 206 ? ResumableDownloadTaskStatus.DownloadedPartial : ResumableDownloadTaskStatus.Downloaded;
                    // 下载完成 或 partial content 写入最终文件
                    handler.SafeFlush();
                    await handler.WriteToFinalFile();
                    return true;
                }

                Debug.Log($"[ResumableDownloadWorker] <StartAsync> failed code={request.responseCode} error={request.error} path={path}");
                status = ResumableDownloadTaskStatus.Failed;
                return false;
            }
            catch (UnityWebRequestException e)
            {
                if (request.responseCode == 416)
                {
                    // header range 有问题，除非本地文件损坏，否则就是因为已经下完了
                    Debug.Log($"[ResumableDownloadWorker] <StartAsync> 416 Range Not Satisfiable. path={path}");
                    status = ResumableDownloadTaskStatus.Downloaded;
                    handler.SafeFlush();
                    handler.RemoveTempFile();
                    return true;
                }
                else if (request.responseCode == 0 && request.error == "Request timeout")
                {
                    // 连接超时，未收到任何返回
                    Debug.Log($"[ResumableDownloadWorker] <StartAsync> request timeout code={request.responseCode} path={path}");
                    status = ResumableDownloadTaskStatus.DownloadedPartial;
                    handler.SafeFlush();
                    handler.RemoveTempFile();
                    return false;
                }
                else if ((request.responseCode == 200 || request.responseCode == 206) && request.error == "Request timeout")
                {
                    // 请求超时，下了一部分内容
                    Debug.Log($"[ResumableDownloadWorker] <StartAsync> request timeout code={request.responseCode} path={path}");
                    status = ResumableDownloadTaskStatus.DownloadedPartial;
                    handler.SafeFlush();
                    await handler.WriteToFinalFile();
                    return false;
                }

                Debug.LogError($"[ResumableDownloadWorker] <StartAsync> UnityWebRequestException: {request.responseCode} {request.error} {e.Message} path={path}");
                status = ResumableDownloadTaskStatus.Failed;
                return false;
            }
            catch (Exception e)
            {
                if (cancellationToken.IsCancellationRequested || status == ResumableDownloadTaskStatus.Cancelled)
                {
                    Debug.Log($"[ResumableDownloadWorker] <StartAsync> cancelled path={path}");
                    return false;
                }

                Debug.LogError($"[ResumableDownloadWorker] <StartAsync> exception: {e.Message} path={path}");
                status = ResumableDownloadTaskStatus.Failed;
                return false;
            }
            finally
            {
                handler.Dispose();
            }
        }
    }

    /// <summary>
    /// 支持以下功能
    /// 1. 下载写入文件
    /// 2. 断点续传
    /// 3. 进度回调
    /// </summary>
    public class ResumableDownloadHandler : DownloadHandlerScript
    {
        private readonly string mFilePath;
        private readonly string mTempPath;
        private ulong mReceivedBytes = 0;
        private ulong mTotalBytes;
        private FileStream mFileStream;
        [CanBeNull] private readonly Action<string, ulong, ulong, ResumableDownloadTaskStatus> mOnProgress;

        public ResumableDownloadHandler(
            string filePath,
            [CanBeNull] Action<string, ulong, ulong, ResumableDownloadTaskStatus> onProgress = null
        ) : base(new byte[64 * 1024])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            mFilePath = filePath;
            mTempPath = filePath + ".part";
            mOnProgress = onProgress;
            mFileStream = new FileStream(mTempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        }

        protected override void ReceiveContentLengthHeader(ulong contentLength)
        {
            Debug.Log($"[ResumableDownloadHandler] <ReceiveContentLengthHeader> {mFilePath} {contentLength}");
            if (contentLength > 0)
            {
                mTotalBytes = contentLength;
            }
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return false;
            
            mFileStream.Write(data, 0, dataLength);
            mReceivedBytes += (ulong)dataLength;
            mOnProgress?.Invoke(mFilePath, mReceivedBytes, mTotalBytes, ResumableDownloadTaskStatus.Downloading);
            return true;
        }

        protected override void CompleteContent()
        {
            mOnProgress?.Invoke(mFilePath, mReceivedBytes, mTotalBytes, ResumableDownloadTaskStatus.Downloaded);
        }

        public void RemoveTempFile()
        {
            if (File.Exists(mTempPath))
            {
                File.Delete(mTempPath);
            }
        }

        private async UniTask SafeMoveFileAsync(string src, string dst, int retryCount = 3, int delayMs = 200)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    File.Move(src, dst);
                    return;
                }
                catch (IOException e)
                {
                    if (i == retryCount - 1)
                        throw;

                    Debug.LogWarning($"[SafeMoveFile] retry {i + 1}/{retryCount} due to {e.Message}");
                    await UniTask.Delay(delayMs);
                }
            }
        }

        public async UniTask WriteToFinalFile()
        {
            SafeFlush();

            if (!File.Exists(mTempPath))
            {
                Debug.LogError($"[ResumableDownloadHandler] <WriteToFinalFile> no temp file mTempPath={mTempPath}");
                return;
            }

            if (!File.Exists(mFilePath))
            {
                // 最终文件不存在，temp 文件直接重命名为最终文件
                await SafeMoveFileAsync(mTempPath, mFilePath);
                return;
            }

            // temp 文件合并入最终文件
            for (int i = 1; i <= 3; i++)
            {
                try
                {
                    using (FileStream temp = new(mTempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (FileStream dest = new(mFilePath, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        dest.Seek(0, SeekOrigin.End);
                        temp.CopyTo(dest);
                    }

                    // 删除 temp 文件
                    File.Delete(mTempPath);
                    break;
                }
                catch (IOException e)
                {
                    if (i >= 3)
                    {
                        Debug.LogError($"[ResumableDownloadHandler] <WriteToFinalFile> failed merge file mTempFile={mTempPath}");
                        break;
                    }
                    Debug.LogWarning($"[ResumableDownloadHandler] <WriteToFinalFile> retry merge {i}/3 due to {e.Message} mTempPath={mTempPath}");
                    await UniTask.Delay(200);
                }
            }
        }

        public void SafeFlush()
        {
            if (mFileStream == null) return;
            FileStream stream = mFileStream;
            mFileStream = null;  //  提前将 mFileStream 赋值为 null，减少多次并行调用可能造成的冲突

            try
            {
                if (stream.CanWrite)
                {
                    stream.Flush(true);
                }
            }
            catch (ObjectDisposedException)
            {
                // 已关闭，不需要日志
            }
            catch (IOException e)
            {
                Debug.LogError($"[ResumableDownloadHandler] <FlushAndClose> IOException {e.Message} mTempPath={mTempPath}");
            }
            finally
            {
                stream?.Dispose();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            SafeFlush();
        }
    }
}

public static class ResumableDownloaderDemo
{
    public static async UniTaskVoid RunDownloadManager()
    {
        string persistentDataPath = Application.persistentDataPath;
        Debug.Log("[ResumableDownloaderDemo] <Run>");
        ResumableDownloadManager manager = new(){onProgress = null};

        // string urlS3 = "https://res.pa.recreategames.com.cn/SignedUrlTest/ResumeDownload100MB.bin?Expires=1761163658&Signature=G9SEe-FTFCRTv3QC8M-A0fLtyvOHseWmCkNmH71ZLmK4BGuzEomqsdi~6XwcLXMF8XdhXr-b6r6~ABwWHxzf1ah0sHdDxNP-KpT1f53QbWBYYJLPa1tUimetNZvxTd2FUVwU5tTMB-QiM4b5ndZ~PJjk-sVCo5srHH9gnXNPrIH8b3NPYAeh2X6zRzfYamT6NOSmzp2uSJm2TfdAogcDzWW7RoPgBdKLW0NUfBbkJB4lJqjWDLcf~L3GPIeE1VRwfLG-qDPmqZ6-cumhEGBnYnOlncHPaW2xfQU1cyt0pgbf2r--q8C8lew9elTp2n52lheWKIKKnYg-ukB2azSXwQ__&Key-Pair-Id=K1TH4HUCFECDNW";
        // urlS3 = "https://res.pa.recreategames.com.cn/SignedUrlTest/ResumeDownload1KB.bin?Expires=1761167899&Signature=Y3RxDWglTbJ1QfdgmjOfAyREdo3eLjJkBTnpMF7o3SnrZSKIaf3SmMiN3OSCQQC3VgUI3w-awbr3piTAA9xFj2MBn5VN-kwQ41R-7ms9jhhChUoj2tUScf0NcF7WuA81zAYzxzvV7HGEDe1kvYTIRVsPc4qHESXM0to8eYUtcv8pidhY82m9342JCUnJiPVruO7jO6fH07ZKCqg7sSoB324QyiQhDdckI0eJLu4rECZZ6XcgTeGDWoHssDroMLu484CDJeI2ZeY20-OzjrywL8XDIRGG~hcTFccMdgP7l9L3E0SHPeGlAPLXzrNsmWcCvsms7MUjujBqWl1lkiK3Jg__&Key-Pair-Id=K1TH4HUCFECDNW";
        // urlS3 = "https://res.pa.recreategames.com.cn/SignedUrlTest/ResumeDownloadTiny.bin?Expires=1761598148&Signature=IsybMPJsikKWR1BUjVIBvnz0tmyfG2p91pnHi7I~8Ghy7JPJXR~0TTLSFTcdQ~3ENZHbtTO~fbuxUuDM1rVM21gk~VPO9VVkZEtcmT1~YE5E2vJGOjLFngbSz2zmi0gj63mWtsJfe9oqH~Catdh~eyFfUCBqroMjE0~hwt75DXTVqTbOl0v-Ha7ptmEf-oqB0HmHaXZ9NFhd3e0CeeveK6DqW~blYOnldtrf2t3ydMX25jYWWVaafzVYORZxGycViDJ6aC9SWSviq-KPgTk5Hj7aG60-QLsTyZdT1Rq57eYh5OMQvWKOHoJ031Rvt~SoQ6F65nDent2BlGNT8UWQ9A__&Key-Pair-Id=K1TH4HUCFECDNW";
        // manager.AddDownloadTask(urlS3, Path.Combine(persistentDataPath, "file1.bin"));

        string urlS3Tiny = "https://res.pa.recreategames.com.cn/SignedUrlTest/ResumeDownloadTiny.bin?Expires=1761685847&Signature=UmFVdOB0fC1uE53sU8IGrb2vqE4e9tU5AEwvfynD-juCq6DblZxnh~kOA39DljO8d2Wrb66iGEf4RGvDxfIB78lbaryIIBghdXALuLsPkmLPi~oYVs5P1xli0MZ~BmlENOMV9sF44bH1lsxBLFmhH5CvBzApz0ZZzkAAjy0m0X~TGfOMeAo9mBT-nDsl3~-0~KTMD6en-SBhojXHFNeZVD6pCNRAzU5rLuRNWE896eCM90qTqvXRpB0AWoTzwmX4pJ2jOp3XcROW2BJBXxI-xwU1E2aqyPWdofEVwaWF4g3UGXdjWjT7KscFTnAiE1cKWK0dJz4D9871XSabBkQWOQ__&Key-Pair-Id=K1TH4HUCFECDNW";
        manager.AddDownloadTask(urlS3Tiny, Path.Combine(persistentDataPath, $"{nameof(urlS3Tiny)}.bin"), timeout:2);

        // string urlS3Random10K = "https://res.pa.recreategames.com.cn/SignedUrlTest/ResumeDownloadRandom10KB.bin?Expires=1761674736&Signature=VITTSIqfu-6IQ8FzHtFIFIJaLBhF~qM8xzQXz6Teb55h3MuJoRh-ce3W6S979qVzECkHzVsGWkPmXmcGAy1uivSkRCLyLEWY5Nj5fZbZFIWXznzwZM-Gcv3egMMOgdptfshkTdLTQSfeGRaBkyIS0sydyhdPZ8EAMoIPJIMxQIqTfzv~Jz2VNedrGzDbaasSP2ZXStl6WRvwWcvdb-7~Of3iufBcifUS5fJgVbiMGo7hUEWZZ55GCLFz~HEAbT7mGi~r4d~ifeyJ1iYOV9GIpX5eBWnlozn-jApgn5L55Mx6HOxO3p3IiR2Fp9HiJRyhcmheQ6Lrgxsa2XwWUMwsrw__&Key-Pair-Id=K1TH4HUCFECDNW";
        // manager.AddDownloadTask(urlS3Random10K, Path.Combine(persistentDataPath, "urlS3Random10K.bin"), timeout:10);

        string urlS3Random100K = "https://res.pa.recreategames.com.cn/SignedUrlTest/ResumeDownloadRandom100KB.bin?Expires=1761678770&Signature=KNPajXiKZCm~EfBP4TZqkrdJ2KNuUf4bwPioE298yS~tWglSpGrY3~ahJuSAG-~Vno8yxomA8oPaNsro9EFjTSkhhjSBWqpPnzcJ1FxgZ2zkFbrBoTaTvJLEIHWPCSBVg7Dgg3FBfxcovY4JFz22lADwswzCj02UTMH0vt3ADxiEYsC66pCPuyZ58neL1fPRPd0z1mDyCGBVicHrscgdlGoX4bBngCGNKv1uB4nvm3ac8nUFS1QO9CgySYzVb5WjvcWvuG6vPNNqFcJJahT2OliRDhL60dOzTEznA6YWYzBWm2ysIL0YX6K3uj3o3IZw75O-WiLHIqOU-tu3MkLO4Q__&Key-Pair-Id=K1TH4HUCFECDNW";
        manager.AddDownloadTask(urlS3Random100K, Path.Combine(persistentDataPath, $"{nameof(urlS3Random100K)}.bin"), timeout:2);
        
        await manager.DownloadAll();
    }
}
