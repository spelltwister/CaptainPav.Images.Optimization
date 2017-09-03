using System;
using System.Net.Http;
using System.Threading.Tasks;
using CaptainPav.Images.Optimization.Contracts;
using Kraken;
using Kraken.Model;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;

namespace CaptainPav.Images.Optimization.Service.AzureKraken
{
    public class ImageManager : IImageManager
    {
        /// <summary>
        /// The Kraken <see cref="Client" /> used to optimize images
        /// </summary>
        protected Client KrakenClient { get; }

        /// <summary>
        /// The <see cref="HttpClient"/> used to download a copy of the image to optimize
        /// </summary>
        protected static readonly HttpClient DownloaderClient = new HttpClient();

        /// <summary>
        /// The <see cref="CloudTable" /> used to store a record relating the
        /// copied image to the optimized iamge
        /// </summary>
        protected CloudTable ImageTable { get; set; }

        /// <summary>
        /// The <see cref="CloudBlobClient" /> used to store a copy of the
        /// original image along with the optimized version
        /// </summary>
        protected CloudBlobClient ImageBlobClient { get; set; }

        /// <summary>
        /// Creates an image manager along with storage dependencies, if needed
        /// </summary>
        /// <param name="imageStorageAccount">
        /// The storage account used to save original and optimized images
        /// </param>
        /// <param name="imageTableName">
        /// The name of the table storing the relationship between original
        /// and optimized images
        /// </param>
        /// <param name="krakenClient">
        /// The Kraken <see cref="Client" /> used to optimize images
        /// </param>
        /// <returns></returns>
        public static async Task<ImageManager> CreateAsync(CloudStorageAccount imageStorageAccount, string imageTableName, Client krakenClient)
        {
            var tableClient = imageStorageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference(imageTableName);
            await table.CreateIfNotExistsAsync().ConfigureAwait(false);
            return new ImageManager(table, imageStorageAccount.CreateCloudBlobClient(), krakenClient);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageManager"/> class.
        /// </summary>
        /// <param name="imageTable">
        /// The <see cref="CloudTable" /> used to store relationships between
        /// original and optimized images
        /// </param>
        /// <param name="imageBlobClient">
        /// The <see cref="CloudBlobClient" /> used to store the original and
        /// optimized images
        /// </param>
        /// <param name="krakenClient">
        /// The Kraken <see cref="Client" /> used to optimize images
        /// </param>
        public ImageManager(CloudTable imageTable, CloudBlobClient imageBlobClient, Client krakenClient)
        {
            this.ImageTable = imageTable;
            this.ImageBlobClient = imageBlobClient;
            this.KrakenClient = krakenClient;
        }

        /// <inheritdoc />
        public async Task<IImageRecord> GetRowIfExistsAsync(string siteId, string imageUrl)
        {
            return await GetRowIfExistsAsync(this.ImageTable, siteId, imageUrl).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IImageRecord> GetOrSaveRowAsync(string siteId, string imageUrl, string optimziedImageName)
        {
            return await GetOrSaveRowAsync(this.ImageTable, this.ImageBlobClient, this.KrakenClient, siteId, imageUrl, optimziedImageName).ConfigureAwait(false);
        }

        public static async Task<ImageRecord> GetRowIfExistsAsync(CloudTable imageTable, string siteId, string imageUrl)
        {
            try
            {
                return (await imageTable.ExecuteAsync(TableOperation.Retrieve<ImageRecord>(siteId, imageUrl.UrlFriendlyString()))
                                        .ConfigureAwait(false))
                        .Result as ImageRecord;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<ImageRecord> GetOrSaveRowAsync(CloudTable imageTable, CloudBlobClient imagesBlobClient, Client krakenClient, string siteId, string imageUrl, string optimizedImageName)
        {
            System.Diagnostics.Trace.TraceInformation($"Checking for existing row using `{imageTable.Name}` table for `{siteId}` site image `{imageUrl}`.");
            var existingRecord = await GetRowIfExistsAsync(imageTable, siteId, imageUrl).ConfigureAwait(false);
            if (existingRecord != null)
            {
                System.Diagnostics.Trace.TraceInformation($"Found existing row using `{imageTable.Name}` table for `{siteId}` site image `{imageUrl}`.");
                return existingRecord;
            }
            System.Diagnostics.Trace.TraceInformation($"No existing row found using `{imageTable.Name}` table for `{siteId}` site image `{imageUrl}`.");

            System.Diagnostics.Trace.TraceInformation($"Getting bytes for `{siteId}` site image `{imageUrl}`.");

            var siteImagesContainer = imagesBlobClient.GetContainerReference(siteId);
            await siteImagesContainer.CreateIfNotExistsAsync().ConfigureAwait(false);

            var imageBytes = await GetOrSaveImageBytesAsync(siteImagesContainer, siteId, imageUrl, optimizedImageName).ConfigureAwait(false);
            if (imageBytes == null)
            {
                System.Diagnostics.Trace.TraceInformation($"Unable to get bytes for `{siteId}` site image `{imageUrl}`.");
                return null;
            }

            // TODO: check for optimized image first, maybe the record just did not get written

            System.Diagnostics.Trace.TraceInformation($"Optimizing `{siteId}` site image `{imageUrl}`.");
            var optimizedImage = await krakenClient.OptimizeWait(imageBytes.Item2, optimizedImageName,
                new OptimizeUploadWaitRequest()
                {
                    Lossy = true
                }).ConfigureAwait(false);

            if (!optimizedImage.Success)
            {
                throw new InvalidOperationException($"Not able to optimize image `{optimizedImageName}`.  Error: `{optimizedImage.Error}`.");
            }

            System.Diagnostics.Trace.TraceInformation($"Downloading optimized bytes for `{siteId}` site image `{imageUrl}`.");
            var lossyBytes = await GetImageBytesAsync(optimizedImage.Body.KrakedUrl).ConfigureAwait(false);
            System.Diagnostics.Trace.TraceInformation($"Downloaded optimized bytes for `{siteId}` site image `{imageUrl}`.  Unoptimized size: {imageBytes.Item2.Length} bytes, optimized size: {lossyBytes.Length} bytes.");

            System.Diagnostics.Trace.TraceInformation($"Saving optimized bytes for `{siteId}` site image `{imageUrl}`.");
            var optimizedBlob = siteImagesContainer.GetBlockBlobReference($"aaopt/{optimizedImageName}");
            await optimizedBlob.UploadFromByteArrayAsync(lossyBytes, 0, lossyBytes.Length).ConfigureAwait(false);
            System.Diagnostics.Trace.TraceInformation($"Saved optimized bytes for `{siteId}` site image `{imageUrl}`.");

            ImageRecord newRecord = new ImageRecord()
            {
                SiteId = siteId,
                ImageUrl = imageUrl,
                ImageLocalCopyUrl = imageBytes.Item1.Uri.AbsoluteUri,
                OptimizedImageUrl = optimizedBlob.Uri.AbsoluteUri
            };

            System.Diagnostics.Trace.TraceInformation($"Creating image record for `{siteId}` site image `{imageUrl}` original copy `{newRecord.ImageLocalCopyUrl}` optimized copy `{newRecord.OptimizedImageUrl}`.");
            await imageTable.ExecuteAsync(TableOperation.Insert(newRecord)).ConfigureAwait(false);
            System.Diagnostics.Trace.TraceInformation($"Created image record for `{siteId}` site image `{imageUrl}` original copy `{newRecord.ImageLocalCopyUrl}` optimized copy `{newRecord.OptimizedImageUrl}`.");

            return newRecord;
        }

        // TODO: use stream instead of byte[]
        protected static async Task<byte[]> GetImageBytesAsync(string imageUrl)
        {
            System.Diagnostics.Trace.TraceInformation($"Downloading copy of image `{imageUrl}`.");
            try
            {
                var imageResponse = await DownloaderClient.GetAsync(imageUrl).ConfigureAwait(false);
                System.Diagnostics.Trace.TraceInformation($"Response from copying image `{imageUrl}` -> Status: {imageResponse.StatusCode}.");
                imageResponse.EnsureSuccessStatusCode();
                System.Diagnostics.Trace.TraceWarning($"Returning downloaded copy of image `{imageUrl}`.");
                return await imageResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            catch
            {
                System.Diagnostics.Trace.TraceWarning($"Failed to download copy of image `{imageUrl}`.");
                return null;
            }
        }

        protected static async Task<Tuple<CloudBlockBlob, byte[]>> GetOrSaveImageBytesAsync(CloudBlobContainer siteImagesContainer, string siteId, string imageUrl, string optimizedImageName)
        {
            System.Diagnostics.Trace.TraceInformation($"Checking for local copy of `{siteId}` site image `{imageUrl}`.");

            var copyBlob = siteImagesContainer.GetBlockBlobReference($"original/{optimizedImageName}");
            if (await copyBlob.ExistsAsync().ConfigureAwait(false))
            {
                await copyBlob.FetchAttributesAsync().ConfigureAwait(false);
                System.Diagnostics.Trace.TraceInformation($"Found local copy of `{siteId}` site image `{imageUrl}` with size {copyBlob.Properties.Length} bytes.");
                if (copyBlob.Properties.Length > 0)
                {
                    byte[] localBytes = new byte[copyBlob.Properties.Length];
                    if (await copyBlob.DownloadToByteArrayAsync(localBytes, 0).ConfigureAwait(false) != localBytes.Length)
                    {
                        throw new InvalidOperationException($"When trying to download local bytes for `{siteId}` site image `{imageUrl}`, an unexpected number of bytes were downloaded.");
                    }

                    System.Diagnostics.Trace.TraceInformation($"Found local copy of `{siteId}` site image `{imageUrl}` with size {copyBlob.Properties.Length} bytes.");
                    return Tuple.Create(copyBlob, localBytes);
                }

                System.Diagnostics.Trace.TraceWarning($"Local copy of `{siteId}` site image `{imageUrl}` is zero bytes; ignoring.");
            }

            System.Diagnostics.Trace.TraceInformation($"Downloading copy of `{siteId}` site image `{imageUrl}`.");
            var imageBytes = await GetImageBytesAsync(imageUrl).ConfigureAwait(false);
            if (imageBytes == null || imageBytes.Length == 0)
            {
                System.Diagnostics.Trace.TraceWarning($"Failed to download copy of `{siteId}` site image `{imageUrl}`.");
                return null;
            }

            System.Diagnostics.Trace.TraceInformation($"Copying bytes of `{siteId}` site image `{imageUrl}`.");
            await copyBlob.UploadFromByteArrayAsync(imageBytes, 0, imageBytes.Length).ConfigureAwait(false);
            System.Diagnostics.Trace.TraceInformation($"Copied bytes of `{siteId}` site image `{imageUrl}`.");

            return Tuple.Create(copyBlob, imageBytes);
        }
    }
}