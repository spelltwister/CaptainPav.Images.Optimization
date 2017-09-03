using System.Text.RegularExpressions;

namespace CaptainPav.Images.Optimization.Service.AzureKraken
{
    public static class StringExtensions
    {
        /// <summary>
        /// Returns a URL friendly version of the input string
        /// </summary>
        /// <param name="input">
        /// The string for which to get a UrlFriendlyString
        /// </param>
        /// <returns>
        /// A URL friendly version of the input string
        /// </returns>
        /// <remarks>
        /// This method does not encode the string; it removes characters
        /// in order to produce a more user friendly string
        /// </remarks>
        public static string UrlFriendlyString(this string input)
        {
            return
                Regex.Replace(
                    Regex.Replace(
                        Regex.Replace(
                            Regex.Replace(input, "[^a-zA-Z0-9 -]", ""), // remove invalid characters
                            "\\s+", " "), // reduce spaces
                        "\\s", "-"), // replace spaces
                    "--+", "-") // reduce dashes
                    .Trim('-'); // remove leading and trailing '-'
        }
    }
}