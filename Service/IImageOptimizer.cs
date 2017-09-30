using System.Threading.Tasks;

namespace CaptainPav.Images.Optimization.Service
{
    public interface IImageOptimizer
    {
        Task<byte[]> OptimizeBytesAsync(byte[] original, string imageName);
    }
}