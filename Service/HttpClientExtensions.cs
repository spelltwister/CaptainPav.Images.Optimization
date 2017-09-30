using System.Net.Http;
using System.Threading.Tasks;

namespace CaptainPav.Images.Optimization.Service
{
    public static class HttpClientExtensions
    {
        public static async Task<byte[]> GetImageBytesAsync(this HttpClient client, string imageUrl)
        {
            System.Diagnostics.Trace.TraceInformation($"Downloading copy of image `{imageUrl}`.");
            try
            {
                var imageResponse = await client.GetAsync(imageUrl).ConfigureAwait(false);
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
    }
}