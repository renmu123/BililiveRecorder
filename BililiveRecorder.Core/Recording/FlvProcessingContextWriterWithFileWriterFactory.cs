using System;
using BililiveRecorder.Flv;
using BililiveRecorder.Flv.Writer;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BililiveRecorder.Core.Recording
{
    internal class FlvProcessingContextWriterWithFileWriterFactory : IFlvProcessingContextWriterFactory
    {
        private readonly IServiceProvider serviceProvider;

        public FlvProcessingContextWriterWithFileWriterFactory(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IFlvProcessingContextWriter CreateWriter(IFlvWriterTargetProvider targetProvider, bool allowMissingHeader = false)
        {
            var logger = this.serviceProvider.GetService<ILogger>();
            return new FlvProcessingContextWriter(
                            tagWriter: new FlvTagFileWriter(targetProvider: targetProvider,
                                                            memoryStreamProvider: this.serviceProvider.GetRequiredService<IMemoryStreamProvider>(),
                                                            logger: logger),
                            allowMissingHeader: allowMissingHeader,
                            disableKeyframes: false,
                            logger: logger);
        }
    }
}
