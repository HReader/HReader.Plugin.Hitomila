using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HReader.Base;
using HReader.Base.Data;

// It is conventional to use the <Your Name>.Plugin.<Your Source Name> namespace
// For third-party or non-builtin metadata or content sources. Your full assembly
// name should end with .hrs.dll (which means ending with .hrs in visual studio)
// You cannot add additional dependencies, they will not be loaded at runtime.
namespace HReader.Plugin.Hitomila
{
    // Implement the IMetadataSource interface to create a new metadata source.
    // The class must be public and have a public parameterless constructor.
    // Be aware that both the CandleHandle and HandleAsync methods MUST be thread-safe
    // because they can be called at the same time by HReader.
    public class MetadataSource : IMetadataSource
    {
        // this information identifies the source to HReader
        public string Name    => "Hitomi.la";
        public string Author  => "HReader";
        public string Version => "0.1.2-alpha"; //convention: use the SemVer versioning scheme

        public Uri    Website { get; } = new Uri("https://github.com/HReader/HReader.Plugin.Hitomila", UriKind.Absolute);

        // This method is called by HReader when it attempts to resolve a uri to metadata.
        // It should return true if this source is able to handle the given uri.
        // It should not start long-running operations as many of them may be called in a row.
        public bool CanHandle(Uri uri)
        {
            // first check we're actually attempting to resolve web content
            if(!(  uri.Scheme.Equals("http",  StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // make sure we're on the right host first
            // make sure we're loading a html document second
            // check if the uri is known to contain the gallery id necessary to load metadata
            return     uri.IdnHost  .Equals(    "hitomi.la",   StringComparison.OrdinalIgnoreCase)
                   &&  uri.LocalPath.EndsWith(  ".html",       StringComparison.OrdinalIgnoreCase)
                   && (uri.LocalPath.StartsWith("/galleries/", StringComparison.OrdinalIgnoreCase)
                   ||  uri.LocalPath.StartsWith("/reader/",    StringComparison.OrdinalIgnoreCase));
        }
        
        // this method is called when metadata should be resolved from a uri
        // All uris passed into the method have previously been checked using the CanHandle(Uri) method
        public async Task<IMetadata> HandleAsync(Uri uri)
        {
            // sanatize the uri to always point to a gallery info directly
            uri = SanatizeUri(uri);

            // use the built-in Utilities class to simplify network requests
            var doc = await Utilities.GetHtmlAsync(uri);

            // parse html using the AngleSharp dependency
            var gallery = doc.QuerySelector("div.gallery");

            var title = gallery.QuerySelector("h1").TextSane();

            var artists = gallery.QuerySelector("h2")
                                 .QuerySelectorAll("li")
                                 .Select(li => new Artist(li.TextSane()))
                                 .ToImmutableList();

            var infoTable = gallery.QuerySelector("table")
                                   .QuerySelectorAll("tr")
                                   .ToImmutableList();

            var kind = new Kind(infoTable[1].QuerySelector("a").TextSane());
            var language = new Language(infoTable[2].QuerySelector("a").TextSane());

            var series = infoTable[3].QuerySelectorAll("li")
                                     .Select(li => new Series(li.TextSane()))
                                     .ToImmutableList();

            var characters = infoTable[3].QuerySelectorAll("li")
                                     .Select(li => new Character(li.TextSane()))
                                     .ToImmutableList();

            var tags = infoTable[3].QuerySelectorAll("li")
                                   .Select(li => new Tag(li.TextSane()))
                                   .ToImmutableList();

            // the thumbnail links aren't all in html but paginated every 50 pages
            // they are however all available in a javascript array immediately
            // we use regex to find all the thumbnails and extract the actual file names
            // of the full quality pictures from them
            var thumbnailScript = doc.QuerySelector("div.gallery-preview > script")
                                     .TextSane();

            var matches = ThumbnailUrlRegex.Matches(thumbnailScript);

            var pageBuilder = ImmutableList.CreateBuilder<Uri>();
            foreach (var match in matches.OfType<Match>())
            {
                pageBuilder.Add(GetPageFromMatch(match));
            }

            //build the metadata to return from all the parts selected
            return new DefaultMetadata(kind, language, series, characters, title, artists, tags, pageBuilder.ToImmutable(), pageBuilder[0]);
        }
        
        private static Uri SanatizeUri(Uri uri)
        {
            var builder = new UriBuilder(uri);
            builder.Path = builder.Path.Replace("/reader/", "/galleries/");
            return builder.Uri;
        }

        // extracts id and filename from a url fragment like
        //       '//tn.hitomi.la/smalltn/1083230/1.jpg.jpg',
        // note the duplicate file extension that is removed here by removing the last 4 characters from the group
        // the duplicate extension probably comes from server-side thumbnail generation that appends the extension without removing the old one
        // group[0] = entire line including trailing comma
        // group[1] = gallery id
        // group[2] = original file name
        private static readonly Regex ThumbnailUrlRegex = new Regex("^'.+\\/(.+)\\/(.+).{4}',$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static Uri GetPageFromMatch(Match match)
        {
            return new UriBuilder
            {
                Scheme = "https",
                Host = "aa.hitomi.la",
                Path = $"/galleries/{match.Groups[1].Value}/{match.Groups[2].Value}"
            }.Uri;
        }
    }
}
