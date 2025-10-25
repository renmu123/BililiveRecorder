using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core.Event;
using BililiveRecorder.Core.Recording;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Polly;
using Serilog;
using Serilog.Events;
using Timer = System.Timers.Timer;

namespace BililiveRecorder.Core
{
    internal class Downloader : IDownloader
    {

        private readonly object recordStartLock = new object();
        private readonly SemaphoreSlim recordRetryDelaySemaphoreSlim = new SemaphoreSlim(1, 1);
        //private readonly Timer timer;

        private readonly IServiceScope scope;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger loggerWithoutContext;
        //private readonly IDanmakuClient danmakuClient;
        //private readonly IApiClient apiClient;
        //private readonly IBasicDanmakuWriter basicDanmakuWriter;
        //private readonly IRecordTaskFactory recordTaskFactory;
        //private readonly UserScriptRunner userScriptRunner;
        //private readonly CancellationTokenSource cts;
        //private readonly CancellationToken ct;

        private ILogger logger;
        private bool disposedValue;

        private int shortId;
        private string name = string.Empty;
        private long uid;
        private string title = string.Empty;
        private string areaNameParent = string.Empty;
        private string areaNameChild = string.Empty;
        //private bool danmakuConnected;
        private bool streaming;
        private bool autoRecordForThisSession = true;

        private IRecordTask? recordTask;
        //private DateTimeOffset danmakuClientConnectTime;
        //private readonly ManualResetEventSlim danmakuConnectHoldOff = new();

        //private static readonly TimeSpan danmakuClientReconnectNoDelay = TimeSpan.FromMinutes(1);
        //private static readonly HttpClient coverDownloadHttpClient = new HttpClient();

        //static Downloader()
        //{
        //    coverDownloadHttpClient.Timeout = TimeSpan.FromSeconds(10);
        //    coverDownloadHttpClient.DefaultRequestHeaders.UserAgent.Clear();
        //}

        public Downloader(IServiceScope scope, DownloaderConfig downloaderConfig, ILogger logger)
        {
            this.scope = scope ?? throw new ArgumentNullException(nameof(scope));
            this.serviceProvider = scope.ServiceProvider;
            this.DownloaderConfig = downloaderConfig ?? throw new ArgumentNullException(nameof(downloaderConfig));
            this.loggerWithoutContext = logger?.ForContext<Room>() ?? throw new ArgumentNullException(nameof(logger));
            this.logger = this.loggerWithoutContext.ForContext(LoggingContext.RoomId, 0);
            //this.danmakuClient = danmakuClient ?? throw new ArgumentNullException(nameof(danmakuClient));
            //this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            //this.basicDanmakuWriter = basicDanmakuWriter ?? throw new ArgumentNullException(nameof(basicDanmakuWriter));
            //this.recordTaskFactory = recordTaskFactory ?? throw new ArgumentNullException(nameof(recordTaskFactory));
            //this.userScriptRunner = userScriptRunner ?? throw new ArgumentNullException(nameof(userScriptRunner));

            //this.timer = new Timer(this.RoomConfig.TimingCheckInterval * 1000d);
            //this.cts = new CancellationTokenSource();
            //this.ct = this.cts.Token;

            //this.PropertyChanged += this.Room_PropertyChanged;
            //this.RoomConfig.PropertyChanged += this.RoomConfig_PropertyChanged;

            //this.timer.Elapsed += this.Timer_Elapsed;

            //this.danmakuClient.StatusChanged += this.DanmakuClient_StatusChanged;
            //this.danmakuClient.DanmakuReceived += this.DanmakuClient_DanmakuReceived;
            //this.danmakuClient.BeforeHandshake = this.DanmakuClient_BeforeHandshake;

            //_ = Task.Run(async () =>
            //{
            //    await Task.Delay(1500 + (initDelayFactor * 500));
            //    this.timer.Start();
            //    await this.RefreshRoomInfoAsync();
            //});
        }

        public int ShortId { get => this.shortId; private set => this.SetField(ref this.shortId, value); }
        public string Name { get => this.name; private set => this.SetField(ref this.name, value); }
        public long Uid { get => this.uid; private set => this.SetField(ref this.uid, value); }
        public string Title { get => this.title; private set => this.SetField(ref this.title, value); }
        public string AreaNameParent { get => this.areaNameParent; private set => this.SetField(ref this.areaNameParent, value); }
        public string AreaNameChild { get => this.areaNameChild; private set => this.SetField(ref this.areaNameChild, value); }

        public JObject? RawBilibiliApiJsonData { get; private set; }

        //public bool Streaming { get => this.streaming; private set => this.SetField(ref this.streaming, value); }

        public bool AutoRecordForThisSession { get => this.autoRecordForThisSession; private set => this.SetField(ref this.autoRecordForThisSession, value); }

        //public bool DanmakuConnected { get => this.danmakuConnected; private set => this.SetField(ref this.danmakuConnected, value); }

        public bool Recording => this.recordTask != null;

        public DownloaderConfig DownloaderConfig { get; }
        public RoomStats Stats { get; } = new RoomStats();

        public Guid ObjectId { get; } = Guid.NewGuid();

        public event EventHandler<RecordSessionStartedEventArgs>? RecordSessionStarted;
        public event EventHandler<RecordSessionEndedEventArgs>? RecordSessionEnded;
        public event EventHandler<RecordFileOpeningEventArgs>? RecordFileOpening;
        public event EventHandler<RecordFileClosedEventArgs>? RecordFileClosed;
        public event EventHandler<IOStatsEventArgs>? IOStats;
        public event EventHandler<RecordingStatsEventArgs>? RecordingStats;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void SplitOutput()
        {
            if (this.disposedValue)
                return;

            lock (this.recordStartLock)
            {
                this.recordTask?.SplitOutput();
            }
        }

        public async Task StartRecord(IServiceProvider sp)
        {
            if (this.disposedValue)
                return;

            //lock (this.recordStartLock)
            {
                this.AutoRecordForThisSession = true;

                {
                    try
                    {
                        await this.CreateAndStartNewRecordTask(DownloaderConfig.Url, DownloaderConfig.OutputPath);
                    }
                    catch (Exception ex)
                    {
                        this.logger.Write(ex is ExecutionRejectedException ? LogEventLevel.Verbose : LogEventLevel.Warning, ex, "尝试开始录制时出错");
                    }
                }
            }
        }

        public void StopRecord()
        {
            if (this.disposedValue)
                return;

            lock (this.recordStartLock)
            {
                this.AutoRecordForThisSession = false;

                if (this.recordTask == null)
                    return;

                this.recordTask.RequestStop();
            }
        }

        #region Recording

        ///
        private async Task CreateAndStartNewRecordTask(string url, string dstPath)
        {
            //lock (this.recordStartLock)
            {
                if (this.disposedValue)
                    return;

                if (this.recordTask != null)
                    return;

                //var task = this.recordTaskFactory.CreateRecordTask(null);
                ObjectFactory factory = ActivatorUtilities.CreateFactory(typeof(DownloaderRecordTask), new[] { typeof(IDownloader) });
                var task = (IRecordTask)factory(this.serviceProvider, new[] { this });
                this.recordTask = task;

                // 订阅事件
                task.IOStats += this.RecordTask_IOStats;
                task.RecordingStats += this.RecordTask_RecordingStats;
                task.RecordFileOpening += this.RecordTask_RecordFileOpening;
                task.RecordFileClosed += this.RecordTask_RecordFileClosed;
                task.RecordSessionEnded += this.RecordTask_RecordSessionEnded;

                //await Task.Run(async () =>
                {
                    try
                    {
                        await task.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        this.logger.Write(ex is ExecutionRejectedException ? LogEventLevel.Verbose : LogEventLevel.Warning, ex, "开始录制时出错");
                        this.recordTask = null;
                        //this.RestartAfterRecordTaskFailedAsync(RestartRecordingReason.GenericRetry);
                    }
                    //RecordSessionStarted?.Invoke(this, new RecordSessionStartedEventArgs(this)
                    //{
                    //    SessionId = this.recordTask.SessionId
                    //});
                }
            }
        }

        ///
        //private async Task RestartAfterRecordTaskFailedAsync(RestartRecordingReason restartRecordingReason)
        //{
        //    if (this.disposedValue)
        //        return;
        //    if (!this.Streaming || !this.AutoRecordForThisSession)
        //        return;

        //    try
        //    {
        //        if (!await this.recordRetryDelaySemaphoreSlim.WaitAsync(0).ConfigureAwait(false))
        //            return;

        //        try
        //        {
        //            var delay = restartRecordingReason switch
        //            {
        //                RestartRecordingReason.GenericRetry => this.RoomConfig.TimingStreamRetry,
        //                RestartRecordingReason.NoMatchingQnValue => this.RoomConfig.TimingStreamRetryNoQn * 1000,
        //                _ => throw new InvalidOperationException()
        //            };
        //            await Task.Delay((int)delay, this.ct).ConfigureAwait(false);
        //        }
        //        catch (TaskCanceledException)
        //        {
        //            // 房间已经被删除
        //            return;
        //        }
        //        finally
        //        {
        //            _ = this.recordRetryDelaySemaphoreSlim.Release();
        //        }

        //        // 如果状态是非直播中，跳过重试尝试。当状态切换到直播中时会开始新的录制任务。
        //        if (!this.Streaming || !this.AutoRecordForThisSession)
        //            return;

        //        // 启动录制时更新房间信息
        //        if (this.Streaming && this.AutoRecordForThisSession)
        //            this.CreateAndStartNewRecordTask(skipFetchRoomInfo: false);
        //    }
        //    catch (Exception ex)
        //    {
        //        this.logger.Write(ex is ExecutionRejectedException ? LogEventLevel.Verbose : LogEventLevel.Warning, ex, "重试开始录制时出错");
        //        _ = Task.Run(() => this.RestartAfterRecordTaskFailedAsync(restartRecordingReason));
        //    }
        //}

        #endregion

        #region Event Handlers

        private void RecordTask_IOStats(object? sender, IOStatsEventArgs e)
        {
            this.Stats.StreamHost = e.StreamHost;
            this.Stats.StartTime = e.StartTime;
            this.Stats.EndTime = e.EndTime;
            this.Stats.Duration = e.Duration;
            this.Stats.NetworkBytesDownloaded = e.NetworkBytesDownloaded;
            this.Stats.NetworkMbps = e.NetworkMbps;
            this.Stats.DiskWriteDuration = e.DiskWriteDuration;
            this.Stats.DiskBytesWritten = e.DiskBytesWritten;
            this.Stats.DiskMBps = e.DiskMBps;
            IOStats?.Invoke(this, e);
        }

        private void RecordTask_RecordingStats(object? sender, RecordingStatsEventArgs e)
        {
            this.Stats.SessionDuration = TimeSpan.FromMilliseconds(e.SessionDuration);
            this.Stats.TotalInputBytes = e.TotalInputBytes;
            this.Stats.TotalOutputBytes = e.TotalOutputBytes;
            this.Stats.CurrentFileSize = e.CurrentFileSize;
            this.Stats.SessionMaxTimestamp = TimeSpan.FromMilliseconds(e.SessionMaxTimestamp);
            this.Stats.FileMaxTimestamp = TimeSpan.FromMilliseconds(e.FileMaxTimestamp);
            this.Stats.AddedDuration = e.AddedDuration;
            this.Stats.PassedTime = e.PassedTime;
            this.Stats.DurationRatio = e.DurationRatio;

            this.Stats.InputVideoBytes = e.InputVideoBytes;
            this.Stats.InputAudioBytes = e.InputAudioBytes;

            this.Stats.OutputVideoFrames = e.OutputVideoFrames;
            this.Stats.OutputAudioFrames = e.OutputAudioFrames;
            this.Stats.OutputVideoBytes = e.OutputVideoBytes;
            this.Stats.OutputAudioBytes = e.OutputAudioBytes;

            this.Stats.TotalInputVideoBytes = e.TotalInputVideoBytes;
            this.Stats.TotalInputAudioBytes = e.TotalInputAudioBytes;

            this.Stats.TotalOutputVideoFrames = e.TotalOutputVideoFrames;
            this.Stats.TotalOutputAudioFrames = e.TotalOutputAudioFrames;
            this.Stats.TotalOutputVideoBytes = e.TotalOutputVideoBytes;
            this.Stats.TotalOutputAudioBytes = e.TotalOutputAudioBytes;

            RecordingStats?.Invoke(this, e);
        }

        private void RecordTask_RecordFileOpening(object? sender, RecordFileOpeningEventArgs e)
        {
            RecordFileOpening?.Invoke(this, e);
        }

        private void RecordTask_RecordFileClosed(object? sender, RecordFileClosedEventArgs e)
        {
            RecordFileClosed?.Invoke(this, e);
        }

        private void RecordTask_RecordSessionEnded(object? sender, EventArgs e)
        {
            var sessionEndedArgs = new RecordSessionEndedEventArgs(null!)
            {
                SessionId = this.recordTask?.SessionId ?? Guid.Empty
            };
            RecordSessionEnded?.Invoke(this, sessionEndedArgs);
            this.recordTask = null;
        }

        #endregion

        #region PropertyChanged

        protected void SetField<T>(ref T location, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(location, value))
                return;

            location = value;

            if (propertyName != null)
                this.OnPropertyChanged(propertyName);
        }

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName: propertyName));

        #endregion

        #region Dispose

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                this.disposedValue = true;
                if (disposing)
                {
                    // dispose managed state (managed objects)
                    //this.cts.Cancel();
                    //this.cts.Dispose();
                    //this.recordTask?.RequestStop();
                    //this.basicDanmakuWriter.Disable();
                    this.scope.Dispose();
                }

                // free unmanaged resources (unmanaged objects) and override finalizer
                // set large fields to null
            }
        }

        // override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~Room()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
