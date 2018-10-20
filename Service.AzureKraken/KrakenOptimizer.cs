using System;
using System.Net.Http;
using System.Threading.Tasks;
using Kraken;
using Kraken.Model;

namespace CaptainPav.Images.Optimization.Service.AzureKraken
{
    public class KrakenOptimizer : IImageOptimizer
    {
        protected HttpClient DownloaderClient { get; }
        protected Client KrakenClient { get; }
        protected OptimizeUploadWaitRequest DefaultRequestType { get; }

        public KrakenOptimizer(Client krakenClient, OptimizeUploadWaitRequest defaultRequestType, HttpClient downloaderClient)
        {
            this.KrakenClient = krakenClient;
            this.DefaultRequestType = defaultRequestType;
            this.DownloaderClient = downloaderClient;
        }

        public Task<byte[]> OptimizeBytesAsync(byte[] original, string imageName)
        {
            return OptimizeBytesAsync(original, imageName, this.DefaultRequestType);
        }

        public async Task<byte[]> OptimizeBytesAsync(byte[] original, string imageName, OptimizeUploadWaitRequest request)
        {
            System.Diagnostics.Trace.TraceInformation($"Sending optimization request for image `{imageName}`.");
            var response =
                await this.KrakenClient
                          .OptimizeWait(original,
                                        imageName,
                                        request)
                          .ConfigureAwait(false);

            if (!response.Success)
            {
                throw new InvalidOperationException($"Not able to optimize image `{imageName}`.  Error: `{response.Error}`.");
            }

            System.Diagnostics.Trace.TraceInformation($"Downloading optimized bytes for image `{imageName}`.");
            var optimizedBytes = await this.DownloaderClient.GetImageBytesAsync(response.Body.KrakedUrl).ConfigureAwait(false);
            System.Diagnostics.Trace.TraceInformation($"Downloaded optimized bytes for image `{imageName}`.  Unoptimized size: {original.Length} bytes, optimized size: {optimizedBytes.Length} bytes.");
            return optimizedBytes;
        }
    }
}