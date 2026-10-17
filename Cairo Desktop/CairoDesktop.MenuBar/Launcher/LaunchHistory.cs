using ManagedShell.Common.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CairoDesktop.MenuBar.Launcher
{
    /// <summary>
    /// How often and how recently each program was launched, for ranking.
    /// </summary>
    public class LaunchHistory
    {
        public class Record
        {
            public int Count { get; set; }

            public DateTime LastUsedUtc { get; set; }
        }

        /// <summary>A launch loses half its weight every two weeks.</summary>
        private const double HalfLifeDays = 14;

        private static readonly string DefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cairo Desktop", "launcher-history.json");

        private readonly string _path;
        private Dictionary<string, Record> _records;

        public LaunchHistory() : this(DefaultPath)
        {
        }

        public LaunchHistory(string path)
        {
            _path = path;
            _records = Load();
        }

        /// <summary>count × decay(lastUsed), scaled to an integer.</summary>
        public int Frecency(string key)
        {
            if (key == null || !_records.TryGetValue(key, out Record record))
            {
                return 0;
            }

            double ageDays = Math.Max(0, (DateTime.UtcNow - record.LastUsedUtc).TotalDays);
            double decay = Math.Pow(0.5, ageDays / HalfLifeDays);
            return (int)Math.Round(record.Count * decay * 100);
        }

        public void RecordLaunch(string key)
        {
            if (key == null)
            {
                return;
            }

            if (!_records.TryGetValue(key, out Record record))
            {
                record = new Record();
                _records[key] = record;
            }

            record.Count++;
            record.LastUsedUtc = DateTime.UtcNow;
            Save();
        }

        private Dictionary<string, Record> Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    return JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(_path)) ?? new Dictionary<string, Record>();
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"LaunchHistory: Unable to load history: {ex.Message}");
            }

            return new Dictionary<string, Record>();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonSerializer.Serialize(_records));
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"LaunchHistory: Unable to save history: {ex.Message}");
            }
        }
    }
}
