namespace CaptainPav.Images.Optimization.Contracts
{
    public interface IImageRecord
    {
        string SiteId { get; }
        string ImageUrl { get; }
        string ImageLocalCopyUrl { get; }
        string OptimizedImageUrl { get; }
    }
}