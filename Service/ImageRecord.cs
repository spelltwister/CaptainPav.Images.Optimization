using CaptainPav.Images.Optimization.Contracts;
using Microsoft.WindowsAzure.Storage.Table;

namespace CaptainPav.Images.Optimization.Service
{
    public class ImageRecord : TableEntity, IImageRecord
    {
        /// <summary>
        /// Gets or sets the ID of the site from which the image was found
        /// </summary>
        public string SiteId
        {
            get => this.PartitionKey;
            set => this.PartitionKey = value;
        }

        private string _imageUrl;
        /// <summary>
        /// Gets or sets the original url of this image
        /// </summary>
        public string ImageUrl
        {
            get => this._imageUrl;
            set
            {
                this._imageUrl = value;
                this.RowKey = value?.UrlFriendlyString();
            }
        }

        /// <summary>
        /// Gets or sets the url of the copied image (unoptimized)
        /// </summary>
        public string ImageLocalCopyUrl { get; set; }

        /// <summary>
        /// Gets or sets the url of the optimized image
        /// </summary>
        public string OptimizedImageUrl { get; set; }
    }
}