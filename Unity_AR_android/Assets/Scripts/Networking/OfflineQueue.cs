using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ParkInspect.Data;

namespace ParkInspect.Networking
{
    /// <summary>
    /// 离线暂存队列（B5）：提交遇到网络失败时把草稿（+现场照片字节）落盘，
    /// 网络恢复后由 ReportPanel 自动逐条重发；成功即出队。
    /// - 存储：Application.persistentDataPath/offline_queue.json（每条含照片 base64，独立文件更省，
    ///   这里照片直接随条目存 base64——现场断网条数少、单张 JPEG 约 100~300KB，可接受）。
    /// - 纯逻辑类（不继承 MonoBehaviour）：文件读写与序列化都在主线程由 ReportPanel 驱动。
    /// </summary>
    public class OfflineQueue
    {
        [Serializable]
        public class Entry
        {
            /// <summary>本地唯一 id：重发成功后凭此清理。</summary>
            public string localId;
            /// <summary>IssueDraft 的 JSON（字段与提交契约一致）。</summary>
            public string draftJson;
            /// <summary>现场照片 JPEG 的 base64；无照片为空串。</summary>
            public string photoBase64;
            /// <summary>入队时间（本地可读格式，仅展示/诊断用）。</summary>
            public string queuedAt;
        }

        [Serializable]
        class Store
        {
            public List<Entry> entries = new List<Entry>();
        }

        readonly string filePath;

        public OfflineQueue()
        {
            filePath = Path.Combine(Application.persistentDataPath, "offline_queue.json");
        }

        /// <summary>当前待重发条数（读盘计数；ReportPanel 每次入队/出队后刷新缓存值）。</summary>
        public int Count()
        {
            return Load().Count;
        }

        public List<Entry> Load()
        {
            try
            {
                if (!File.Exists(filePath)) return new List<Entry>();
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(filePath));
                return store?.entries ?? new List<Entry>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OfflineQueue] 读取离线队列失败（按空队列处理）：" + e.Message);
                return new List<Entry>();
            }
        }

        public void Save(List<Entry> entries)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, JsonUtility.ToJson(new Store { entries = entries }));
            }
            catch (Exception e)
            {
                // 落盘失败不能阻断上报主流程，但必须可见（不静默）。
                Debug.LogWarning("[OfflineQueue] 写入离线队列失败：" + e.Message);
            }
        }

        /// <summary>入队（网络失败时调用）。photoJpeg 可为 null。</summary>
        public Entry Enqueue(IssueDraft draft, byte[] photoJpeg)
        {
            var entries = Load();
            var entry = new Entry
            {
                localId = Guid.NewGuid().ToString("N"),
                draftJson = JsonUtility.ToJson(draft),
                photoBase64 = photoJpeg != null && photoJpeg.Length > 0
                    ? Convert.ToBase64String(photoJpeg)
                    : "",
                queuedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            entries.Add(entry);
            Save(entries);
            return entry;
        }

        /// <summary>出队（重发成功后调用）。</summary>
        public void Remove(string localId)
        {
            var entries = Load();
            entries.RemoveAll(e => e.localId == localId);
            Save(entries);
        }

        /// <summary>取出队首草稿；损坏条目（JSON 非法）直接丢弃并告警，不卡死队列。</summary>
        public bool TryPeek(out Entry entry, out IssueDraft draft)
        {
            entry = null;
            draft = null;
            var entries = Load();
            while (entries.Count > 0)
            {
                var candidate = entries[0];
                IssueDraft d = null;
                try { d = JsonUtility.FromJson<IssueDraft>(candidate.draftJson); }
                catch (Exception) { d = null; }
                if (d != null && !string.IsNullOrEmpty(d.title))
                {
                    entry = candidate;
                    draft = d;
                    return true;
                }
                Debug.LogWarning("[OfflineQueue] 丢弃损坏的离线条目 " + candidate.localId);
                entries.RemoveAt(0);
                Save(entries);
            }
            return false;
        }
    }
}
