using System;
using System.Net.Http;
using System.Threading.Tasks;
using CaptainPav.Images.Optimization.Contracts;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Storage.Blob;

namespace CaptainPav.Images.Optimization.Service
{
    public class ImageManager : IImageManager
    {
        private const string OptimizedFolderPrefix = "aaopt/";

        /// <summary>
        /// The optimizer used to optimize images
        /// </summary>
        protected IImageOptimizer Optimizer { get; }

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
        /// <param name="tableStorageAccount">
        /// The storage account used to save original and optimized images
        /// </param>
        /// <param name="imageTableName">
        /// The name of the table storing the relationship between original
        /// and optimized images
        /// </param>
        /// <param name="optimizer">
        /// The image optimizer used to optimize images
        /// </param>
        /// <returns></returns>
        public static async Task<ImageManager> CreateAsync(CloudStorageAccount tableStorageAccount, Microsoft.Azure.Storage.CloudStorageAccount blobStorageAccount, string imageTableName, IImageOptimizer optimizer)
        {
            var tableClient = tableStorageAccount.CreateCloudTableClient();
            var table = tableClient.GetTableReference(imageTableName);
            await table.CreateIfNotExistsAsync().ConfigureAwait(false);
            return new ImageManager(table, blobStorageAccount.CreateCloudBlobClient(), optimizer);
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
        /// <param name="optimizer">
        /// The image optimizer used to optimize images
        /// </param>
        public ImageManager(CloudTable imageTable, CloudBlobClient imageBlobClient, IImageOptimizer optimizer)
        {
            this.ImageTable = imageTable;
            this.ImageBlobClient = imageBlobClient;
            this.Optimizer = optimizer;
        }

        /// <inheritdoc />
        public async Task<IImageRecord> GetRowIfExistsAsync(string siteId, string imageUrl)
        {
            return await GetRowIfExistsAsync(this.ImageTable, siteId, imageUrl).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<IImageRecord> GetOrSaveRowAsync(string siteId, string imageUrl, string optimziedImageName)
        {
            return await GetOrSaveRowAsync(this.ImageTable, this.ImageBlobClient, this.Optimizer, siteId, imageUrl, optimziedImageName).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<byte[]> GetOptimizedBytesAsync(IImageRecord record)
        {
            var siteImagesContainer = this.ImageBlobClient.GetContainerReference(record.SiteId);
            if (!await siteImagesContainer.ExistsAsync().ConfigureAwait(false))
            {
                throw new ArgumentException("No images for the given site id.");
            }

            var optimizedBlob = siteImagesContainer.GetBlockBlobReference($"{OptimizedFolderPrefix}{record.ImageName}");
            if (!await optimizedBlob.ExistsAsync().ConfigureAwait(false))
            {
                throw new ArgumentException("No image matching the record.");
            }

            await optimizedBlob.FetchAttributesAsync().ConfigureAwait(false);

            System.Diagnostics.Trace.TraceInformation($"Found optimized copy of `{record.SiteId}` site image `{record.ImageUrl}` with size {optimizedBlob.Properties.Length} bytes.");
            if (optimizedBlob.Properties.Length <= 0)
            {
                throw new InvalidOperationException($"Optimized copy of `{record.SiteId}` site image `{record.ImageUrl}` has 0 bytes.");
            }

            byte[] localBytes = new byte[optimizedBlob.Properties.Length];
            if (await optimizedBlob.DownloadToByteArrayAsync(localBytes, 0).ConfigureAwait(false) != localBytes.Length)
            {
                throw new InvalidOperationException($"When trying to download optimized bytes for `{record.SiteId}` site image `{record.ImageUrl}`, an unexpected number of bytes were downloaded.");
            }

            System.Diagnostics.Trace.TraceInformation($"Downloaded optimized copy of `{record.SiteId}` site image `{record.ImageUrl}` with size {optimizedBlob.Properties.Length} bytes.");
            return localBytes;
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

        public static async Task<ImageRecord> GetOrSaveRowAsync(CloudTable imageTable, CloudBlobClient imagesBlobClient, IImageOptimizer optimizer, string siteId, string imageUrl, string optimizedImageName)
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

            // TODO: could be optimized to not download bytes when an optimization is not required

            var imageBytes = await GetOrSaveImageBytesAsync(siteImagesContainer, siteId, imageUrl, optimizedImageName).ConfigureAwait(false);
            if (imageBytes == null)
            {
                System.Diagnostics.Trace.TraceInformation($"Unable to get bytes for `{siteId}` site image `{imageUrl}`.");
                return null;
            }

            var optimizedBlob = await GetOrSaveOptimizedBlobAsync(siteImagesContainer, optimizer, siteId, imageUrl, optimizedImageName, imageBytes.Item2).ConfigureAwait(false);

            ImageRecord newRecord = new ImageRecord()
            {
                SiteId = siteId,
                ImageUrl = imageUrl,
                ImageLocalCopyUrl = imageBytes.Item1.Uri.AbsoluteUri,
                OptimizedImageUrl = optimizedBlob.Uri.AbsoluteUri,
                ImageName = optimizedImageName
            };

            System.Diagnostics.Trace.TraceInformation($"Creating image record for `{siteId}` site image `{imageUrl}` original copy `{newRecord.ImageLocalCopyUrl}` optimized copy `{newRecord.OptimizedImageUrl}`.");
            await imageTable.ExecuteAsync(TableOperation.Insert(newRecord)).ConfigureAwait(false);
            System.Diagnostics.Trace.TraceInformation($"Created image record for `{siteId}` site image `{imageUrl}` original copy `{newRecord.ImageLocalCopyUrl}` optimized copy `{newRecord.OptimizedImageUrl}`.");

            return newRecord;
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
            var imageBytes = await DownloaderClient.GetImageBytesAsync(imageUrl).ConfigureAwait(false);
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

        protected static async Task<CloudBlockBlob> GetOrSaveOptimizedBlobAsync(CloudBlobContainer siteImagesContainer, IImageOptimizer krakenClient, string siteId, string imageUrl, string optimizedImageName, byte[] imageBytes)
        {
            System.Diagnostics.Trace.TraceInformation($"Checking for optimized bytes for `{siteId}` site image `{imageUrl}`.");
            var optimizedBlob = siteImagesContainer.GetBlockBlobReference($"{OptimizedFolderPrefix}{optimizedImageName}");

            bool optimizeExists = await optimizedBlob.ExistsAsync().ConfigureAwait(false);
            if (optimizeExists)
            {
                System.Diagnostics.Trace.TraceInformation($"Optimized bytes exist for `{siteId}` site image `{imageUrl}`.");
                await optimizedBlob.FetchAttributesAsync().ConfigureAwait(false);
                System.Diagnostics.Trace.TraceInformation($"Optimized bytes exist for `{siteId}` site image `{imageUrl}` with size {optimizedBlob.Properties.Length:D}.");
                optimizeExists = optimizedBlob.Properties.Length > 0;
            }

            if (!optimizeExists)
            {
                System.Diagnostics.Trace.TraceInformation($"Optimized bytes do not exist for `{siteId}` site image `{imageUrl}`.");

                System.Diagnostics.Trace.TraceInformation($"Optimizing `{siteId}` site image `{imageUrl}`.");
                var optimizedBytes = await krakenClient.OptimizeBytesAsync(imageBytes, optimizedImageName).ConfigureAwait(false);

                System.Diagnostics.Trace.TraceInformation($"Saving optimized bytes for `{siteId}` site image `{imageUrl}`.");
                await optimizedBlob.UploadFromByteArrayAsync(optimizedBytes, 0, optimizedBytes.Length).ConfigureAwait(false);
                System.Diagnostics.Trace.TraceInformation($"Saved optimized bytes for `{siteId}` site image `{imageUrl}`.");
            }
            else
            {
                System.Diagnostics.Trace.TraceInformation($"Using existing optimized bytes for `{siteId}` site image `{imageUrl}`.");
            }

            return optimizedBlob;
        }
    }
}