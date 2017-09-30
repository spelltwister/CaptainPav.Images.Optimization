using System.Threading.Tasks;

namespace CaptainPav.Images.Optimization.Contracts
{
    public interface IImageManager
    {
        /// <summary>
        /// Gets the image record for the given site image
        /// </summary>
        /// <param name="siteId">
        /// The site partition of the image to find
        /// </param>
        /// <param name="imageUrl">
        /// The image url within the site partition
        /// </param>
        /// <returns>
        /// The image record for the given site image
        /// </returns>
        Task<IImageRecord> GetRowIfExistsAsync(string siteId, string imageUrl);

        /// <summary>
        /// Gets the image record for the given site from storage
        /// or downloads and optimizes the image
        /// </summary>
        /// <param name="siteId">
        /// The site partition of the image
        /// </param>
        /// <param name="imageUrl">
        /// The image url within the site partition
        /// </param>
        /// <param name="optimziedImageName">
        /// The name of the image after optimizing
        /// </param>
        /// <returns>
        /// The image record for the given site image
        /// </returns>
        Task<IImageRecord> GetOrSaveRowAsync(string siteId, string imageUrl, string optimziedImageName);

        /// <summary>
        /// Gets the optimized bytes for the given image record
        /// </summary>
        /// <param name="record">
        /// The record for which to get the optimized bytes
        /// </param>
        /// <returns>
        /// The optimized bytes for the given image record
        /// </returns>
        Task<byte[]> GetOptimizedBytesAsync(IImageRecord record);
    }
}