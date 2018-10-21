namespace CaptainPav.Images.Optimization.Contracts
{
    public interface IImageRecord
    {
        string SiteId { get; }
        string ImageUrl { get; }
        string ImageLocalCopyUrl { get; }
        string OptimizedImageUrl { get; }
        /// <summary>
        /// Gets the name of the image which many inclue segments separated
        /// by '/' like a file path
        /// </summary>
        string ImageName { get; }
    }
}