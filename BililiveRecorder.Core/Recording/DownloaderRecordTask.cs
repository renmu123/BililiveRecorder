using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core.Event;
using BililiveRecorder.Core.ProcessingRules;
using BililiveRecorder.Flv;
using BililiveRecorder.Flv.Parser;
using BililiveRecorder.Flv.Pipeline;
using BililiveRecorder.Flv.Pipeline.Actions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BililiveRecorder.Core.Recording
{
    internal class DownloaderRecordTask : IRecordTask
    {
        protected readonly IDownloader downloader;
        protected readonly ILogger logger;
        //protected readonly IApiClient apiClient;
        //private readonly UserScriptRunner userScriptRunner;
        private readonly IFlvTagReaderFactory flvTagReaderFactory;
        private readonly ITagGroupReaderFactory tagGroupReaderFactory;
        private readonly IFlvProcessingContextWriterFactory writerFactory;
        private readonly ProcessingDelegate pipeline;
        private readonly IFlvWriterTargetProvider targetProvider;

        private readonly StatsRule statsRule;
        private readonly SplitRule splitFileRule;

        private readonly FlvProcessingContext context = new FlvProcessingContext();
        private readonly IDictionary<object, object?> session = new Dictionary<object, object?>();

        private ITagGroupReader? reader;

        public event EventHandler<IOStatsEventArgs>? IOStats;
        public event EventHandler<RecordingStatsEventArgs>? RecordingStats;
        public event EventHandler<RecordFileOpeningEventArgs>? RecordFileOpening;
        public event EventHandler<RecordFileClosedEventArgs>? RecordFileClosed;
        public event EventHandler? RecordSessionEnded;
        private IFlvProcessingContextWriter? writer;

        public Guid SessionId => throw new NotImplementedException();

        public DownloaderRecordTask(
            IDownloader downloader,
            ILogger logger,
            IProcessingPipelineBuilder builder,
            //IApiClient apiClient,
            IFlvTagReaderFactory flvTagReaderFactory,
            ITagGroupReaderFactory tagGroupReaderFactory,
            IFlvProcessingContextWriterFactory writerFactory
            //UserScriptRunner userScriptRunner
        )
        {
            this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            this.logger = logger?.ForContext<DownloaderRecordTask>() ?? throw new ArgumentNullException(nameof(logger));
            //this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.flvTagReaderFactory = flvTagReaderFactory ?? throw new ArgumentNullException(nameof(flvTagReaderFactory));
            //this.userScriptRunner = userScriptRunner ?? throw new ArgumentNullException(nameof(userScriptRunner));

            this.flvTagReaderFactory = flvTagReaderFactory ?? throw new ArgumentNullException(nameof(flvTagReaderFactory));
            this.tagGroupReaderFactory = tagGroupReaderFactory ?? throw new ArgumentNullException(nameof(tagGroupReaderFactory));
            this.writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            this.statsRule = new StatsRule();
            this.splitFileRule = new SplitRule();

            this.statsRule.StatsUpdated += this.StatsRule_StatsUpdated;

            this.pipeline = builder
                .ConfigureServices(services => services.AddSingleton(new ProcessingPipelineSettings
                {
                    SplitOnScriptTag = true,
                }))
                .AddRule(this.statsRule)
                .AddRule(this.splitFileRule)
                .AddDefaultRules()
                .AddRemoveFillerDataRule()
                .Build();

            this.targetProvider = new WriterTargetProvider(this, downloader.DownloaderConfig.OutputPath);

        }

        void IRecordTask.SplitOutput() => throw new NotImplementedException();
        public async Task StartAsync()
        {
            var stream = await this.GetStreamAsync(fullUrl: this.downloader.DownloaderConfig.Url, timeout: 5 * 1000).ConfigureAwait(false);

            //this.ioStatsLastTrigger = DateTimeOffset.UtcNow;
            //this.durationSinceNoDataReceived = TimeSpan.Zero;

//            this.ct.Register(state => Task.Run(async () =>
//            {
//                try
//                {
//                    if (state is not WeakReference<Stream> weakRef)
//                        return;

//                    await Task.Delay(1000);

//                    if (weakRef.TryGetTarget(out var weakStream))
//                    {
//#if NET6_0_OR_GREATER
//                        await weakStream.DisposeAsync();
//#else
//                        weakStream.Dispose();
//#endif
//                    }
//                }
//                catch (Exception)
//                { }
//            }), state: new WeakReference<Stream>(stream), useSynchronizationContext: false);

            await this.StartRecordingLoop(stream);
        }

        void IRecordTask.RequestStop() => throw new NotImplementedException();

        protected async Task<Stream> GetStreamAsync(string fullUrl, int timeout)
        {
            var client = this.CreateHttpClient();
            var streamHostInfoBuilder = new StringBuilder();

            while (true)
            {
                Uri originalUri = new Uri(fullUrl);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, originalUri);
                streamHostInfoBuilder.Append(originalUri.Host);

                var resp = await client
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead
                        //new CancellationTokenSource(timeout).Token
                    );
                    //.ConfigureAwait(false);
                switch (resp.StatusCode)
                {
                    case HttpStatusCode.OK:
                        {
                            this.logger.Information("开始接收直播流");
                            //this.streamHostFull = streamHostInfoBuilder.ToString();
                            var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                            return stream;
                        }
                    case HttpStatusCode.Moved:
                    case HttpStatusCode.Redirect:
                        {
                            fullUrl = new Uri(originalUri, resp.Headers.Location!).ToString();
                            this.logger.Debug("跳转到 {Url}, 原文本 {Location}", fullUrl, resp.Headers.Location!.OriginalString);
                            resp.Dispose();
                            streamHostInfoBuilder.Append('\n');
                            break;
                        }
                    default:
                        throw new Exception(string.Format("尝试下载直播流时服务器返回了 ({0}){1}", resp.StatusCode, resp.ReasonPhrase));
                }
            }
        }
        private HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient(new HttpClientHandler
            {
                UseCookies = false,
                UseDefaultCredentials = false,
                //AllowAutoRedirect = false,
                //UseProxy = this.room.RoomConfig.NetworkTransportUseSystemProxy,
            })
            {
                Timeout = TimeSpan.FromMilliseconds(10000)
            };
            var headers = httpClient.DefaultRequestHeaders;
            var cookie_string = this.downloader.DownloaderConfig.Cookie;
            if (!string.IsNullOrWhiteSpace(cookie_string))
            {
                headers.Add("Cookie", cookie_string);
            }
            if (this.downloader.DownloaderConfig.DownloadHeaders is not null)
            {
                foreach (var header in this.downloader.DownloaderConfig.DownloadHeaders)
                {
                    var headerParts = header.Split(new[] { ':' }, 2); // 指定最大分割数量为2
                    if (headerParts.Length == 2)
                    {
                        headers.Add(headerParts[0], headerParts[1]);
                    }
                    else
                    {
                        this.logger.Warning("下载请求头格式错误：{Header}", header);
                    }
                }
            }

            return httpClient;
        }

        protected async Task StartRecordingLoop(Stream stream)
        {
            var pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));

            this.reader = this.tagGroupReaderFactory.CreateTagGroupReader(this.flvTagReaderFactory.CreateFlvTagReader(pipe.Reader));

            this.writer = this.writerFactory.CreateWriter(this.targetProvider);
            //this.writer.BeforeScriptTagWrite = this.Writer_BeforeScriptTagWrite;
            //this.writer.FileClosed += (sender, e) =>
            //{
            //    var openingEventArgs = (RecordFileOpeningEventArgs)e.State!;
            //    this.OnRecordFileClosed(new RecordFileClosedEventArgs(this.room)
            //    {
            //        SessionId = this.SessionId,
            //        FullPath = openingEventArgs.FullPath,
            //        RelativePath = openingEventArgs.RelativePath,
            //        FileOpenTime = openingEventArgs.FileOpenTime,
            //        FileCloseTime = DateTimeOffset.Now,
            //        Duration = e.Duration,
            //        FileSize = e.FileSize,
            //    });
            //};

            var fillPipeTask = this.FillPipeAsync(stream, pipe.Writer);
            var recordingTask = this.RecordingLoopAsync();
            await Task.WhenAll(fillPipeTask, recordingTask).ConfigureAwait(false);
        }

        private async Task FillPipeAsync(Stream stream, PipeWriter writer)
        {
            const int minimumBufferSize = 1024;
            //this.timer.Start();

            Exception? exception = null;
            try
            {
                //while (!this.ct.IsCancellationRequested)
                while (true)
                {
                    var memory = writer.GetMemory(minimumBufferSize);
                    try
                    {
                        var bytesRead = await stream.ReadAsync(memory).ConfigureAwait(false);
                        if (bytesRead == 0)
                            break;
                        writer.Advance(bytesRead);
                        //_ = Interlocked.Add(ref this.ioNetworkDownloadedBytes, bytesRead);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                        break;
                    }

                    var result = await writer.FlushAsync().ConfigureAwait(false);
                    if (result.IsCompleted)
                        break;
                }
            }
            finally
            {
                //this.timer.Stop();
#if NET6_0_OR_GREATER
                await stream.DisposeAsync().ConfigureAwait(false);
#else
                stream.Dispose();
#endif
                await writer.CompleteAsync(exception).ConfigureAwait(false);
            }
        }

        private async Task RecordingLoopAsync()
        {
            try
            {
                if (this.reader is null) return;
                if (this.writer is null) return;

                //while (!this.ct.IsCancellationRequested)
                while (true)
                {
                    var group = await this.reader.ReadGroupAsync(CancellationToken.None).ConfigureAwait(false);

                    if (group is null)
                        break;

                    this.context.Reset(group, this.session);

                    this.pipeline(this.context);

                    if (this.context.Comments.Count > 0)
                        this.logger.Debug("修复逻辑输出 {@Comments}", this.context.Comments);

                    //this.ioDiskStopwatch.Restart();
                    var bytesWritten = await this.writer.WriteAsync(this.context).ConfigureAwait(false);
                    //this.ioDiskStopwatch.Stop();

                    //lock (this.ioDiskStatsLock)
                    //{
                    //    this.ioDiskWriteDuration += this.ioDiskStopwatch.Elapsed;
                    //    this.ioDiskWrittenBytes += bytesWritten;
                    //}
                    //this.ioDiskStopwatch.Reset();

                    if (this.context.Actions.FirstOrDefault(x => x is PipelineDisconnectAction) is PipelineDisconnectAction disconnectAction)
                    {
                        this.logger.Information("修复系统断开录制：{Reason}", disconnectAction.Reason);
                        break;
                    }
                }
            }
            catch (UnsupportedCodecException ex)
            {
                // 直播流不是 H.264
                this.logger.Warning(ex, "不支持此直播流的视频编码格式（只支持 H.264），本场直播不再自动启动录制。");
                //this.room.StopRecord(); // 停止自动重试
            }
            catch (OperationCanceledException ex)
            {
                this.logger.Debug(ex, "录制被取消");
            }
            catch (IOException ex)
            {
                this.logger.Warning(ex, "录制时发生IO错误");
            }
            catch (Exception ex)
            {
                this.logger.Warning(ex, "录制时发生了错误");
            }
            finally
            {
                this.reader?.Dispose();
                this.reader = null;
                this.writer?.Dispose();
                this.writer = null;
                //this.RequestStop();

                //this.OnRecordSessionEnded(EventArgs.Empty);

                this.logger.Information("录制结束");
            }
        }

        private void StatsRule_StatsUpdated(object? sender, RecordingStatsEventArgs e)
        {
            var maxDuration = this.downloader.DownloaderConfig.MaxDuration;
            var maxSize = this.downloader.DownloaderConfig.MaxSize;

            // 按时长分割
            if (maxDuration.HasValue && maxDuration.Value > 0)
            {
                if (e.FileMaxTimestamp > maxDuration.Value * 60u * 1000u)
                    this.splitFileRule.SetSplitBeforeFlag();
            }
            
            // 按大小分割
            if (maxSize.HasValue && maxSize.Value > 0)
            {
                if ((e.CurrentFileSize + (e.OutputVideoBytes * 1.1) + e.OutputAudioBytes) / (1024d * 1024d) > maxSize.Value)
                    this.splitFileRule.SetSplitBeforeFlag();
            }

            // this.OnRecordingStats(e);
        }

        internal class WriterTargetProvider : IFlvWriterTargetProvider
        {
            private readonly DownloaderRecordTask task;
            private readonly string dstPath;
            private int partIndex = 0;
            private string last_path = string.Empty;

            public WriterTargetProvider(DownloaderRecordTask task, string dstPath)
            {
                this.task = task ?? throw new ArgumentNullException(nameof(task));
                this.dstPath = dstPath ?? throw new ArgumentNullException(nameof(dstPath));
            }

            public (Stream stream, object? state) CreateOutputStream()
            {
                var directory = Path.GetDirectoryName(this.dstPath)!;
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(this.dstPath);
                var extension = Path.GetExtension(this.dstPath);
                
                var fullPath = Path.Combine(directory, $"{fileNameWithoutExtension}_PART{this.partIndex:D3}{extension}");
                this.partIndex++;

                try
                { _ = Directory.CreateDirectory(directory); }
                catch (Exception) { }

                this.last_path = fullPath;
                this.task.logger.Information("创建录制文件 '{Path}'", fullPath);

                var stream = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
                return (stream, null);
            }

            public Stream CreateAccompanyingTextLogStream()
            {
                var path = string.IsNullOrWhiteSpace(this.last_path)
                    ? Path.ChangeExtension(this.dstPath, "txt")
                    : Path.ChangeExtension(this.last_path, "txt");

                try
                { _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
                catch (Exception) { }

                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                return stream;
            }
        }
    }
}
