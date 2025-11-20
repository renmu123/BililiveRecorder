using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BililiveRecorder.Core.Event;

namespace BililiveRecorder.Core
{
    public interface IDownloader : IDisposable
    {
        DownloaderConfig DownloaderConfig { get; }

        Task StartRecord(IServiceProvider sp);

        event EventHandler<RecordSessionStartedEventArgs>? RecordSessionStarted;
        event EventHandler<RecordSessionEndedEventArgs>? RecordSessionEnded;
        event EventHandler<RecordFileOpeningEventArgs>? RecordFileOpening;
        event EventHandler<RecordFileClosedEventArgs>? RecordFileClosed;
        event EventHandler<IOStatsEventArgs>? IOStats;
        event EventHandler<RecordingStatsEventArgs>? RecordingStats;
    }

    public class DownloaderConfig
    {
        public readonly string? Cookie;
        public readonly IEnumerable<string>? DownloadHeaders;
        public readonly string Url;
        public readonly string OutputPath;
        public readonly double? MaxSize;
        public readonly double? MaxDuration;
        public readonly bool UseSystemProxy;
        public readonly string? Proxy;
        public readonly uint TimingWatchdogTimeout;
        public readonly bool SplitOnScriptTag;
        public readonly bool DisableSplitOnH264AnnexB;
        public DownloaderConfig(string url, string outputPath, string? cookie, IEnumerable<string>? downloadHeaders, double? maxSize = null, double? maxDuration = null, bool useSystemProxy = false, string? proxy = null, uint timingWatchdogTimeout = 10000, bool splitOnScriptTag = false, bool disableSplitOnH264AnnexB = false)
        {
            Url = url;
            OutputPath = outputPath;
            this.Cookie = cookie;
            this.DownloadHeaders = downloadHeaders;
            this.MaxSize = maxSize;
            this.MaxDuration = maxDuration;
            this.UseSystemProxy = useSystemProxy;
            this.Proxy = proxy;
            this.TimingWatchdogTimeout = timingWatchdogTimeout;
            this.SplitOnScriptTag = splitOnScriptTag;
            this.DisableSplitOnH264AnnexB = disableSplitOnH264AnnexB;
        }
    }
}
