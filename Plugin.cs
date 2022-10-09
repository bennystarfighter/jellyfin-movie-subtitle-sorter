using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SubtitleFixer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleFixer
{
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public override string Name => "Movie subtitle sorter";

        public override Guid Id => Guid.Parse("786e0827-ed4b-4cbc-870b-12c186f47894");

        public override string Description =>
            "Looks through all movie folders for subtitles hidden inside subfolders. If found it will try to link/copy them to the folder of the movie so Jellyfin discovers them properly.";
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
            xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }
    }

    public class SubtitleFixer : ILibraryPostScanTask
    {
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILibraryManager _libraryManager;
        private readonly ISubtitleManager _subtitleManager;
        private readonly IMediaSourceManager _mediaSourceManager;

        // private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<SubtitleFixer> _logger;

        private readonly IFileSystem _fileSystem;
        // private readonly IProviderManager _providerManager;
        // private readonly NamingOptions _namingOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="OrganizerScheduledTask"/> class.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented",
            Justification = "Parameter types/names are self-documenting")]
        public SubtitleFixer(
            ILibraryMonitor libraryMonitor,
            ILibraryManager libraryManager,
            ILoggerFactory loggerFactory,
            IFileSystem fileSystem,
            ISubtitleManager subtitleManager,
            IMediaSourceManager mediaSourceManager
        )
        {
            _libraryMonitor = libraryMonitor;
            _libraryManager = libraryManager;
            _logger = loggerFactory.CreateLogger<SubtitleFixer>();
            _fileSystem = fileSystem;
            _subtitleManager = subtitleManager;
            _mediaSourceManager = mediaSourceManager;
        }
        
        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Running Subtitle Fixer");
            var allVirtualFolders = _libraryManager.GetVirtualFolders();

            // Run for movies
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                OrderBy = new List<ValueTuple<string, SortOrder>>
                    { new ValueTuple<string, SortOrder>(ItemSortBy.SortName, SortOrder.Ascending) },
                Recursive = true
            };
            var allMovies = _libraryManager.GetItemList(query, false)
                .Select(m => m as MediaBrowser.Controller.Entities.Movies.Movie).ToList();

            var moviesFound = allMovies.Count;
            var completedCount = 0;
            _logger.LogInformation("Found [{0}] eligible movies", moviesFound);

            foreach (var movie in allMovies)
            {
                _logger.LogDebug("Checking movie: {0}", movie.Name);
                _logger.LogDebug("Movie path: {0}", movie.Path);
                if (string.IsNullOrEmpty(movie.Path))
                {
                    continue;
                }

                var movieDir = System.IO.Directory.GetParent(movie.Path);
                if (movieDir == null)
                {
                    continue;
                }

                var movieParent = movieDir.FullName;

                // Check if movie is in root folder of any movie library location.
                var isInRoot = false;
                foreach (var virtualFolder in allVirtualFolders)
                {
                    foreach (var location in virtualFolder.Locations)
                    {
                        if (location == movieParent)
                        {
                            isInRoot = true;
                        }
                    }
                }

                // We dont process movies directly in the root folder.
                // Causes root folder to fill upp with all subtitles from the actual movie subfolders.
                if (isInRoot)
                {
                    continue;
                }

                bool hasNewSubtitle = false;
                
                var dirName = System.IO.Path.GetDirectoryName(movie.Path);
                if (dirName != null)
                    // loop through subfolder inside movie folder
                    foreach (var subFolder in System.IO.Directory.GetDirectories(dirName))
                    {
                        // Check all files in subfolder
                        foreach (var potentialSubFile in System.IO.Directory.GetFiles(subFolder))
                        {
                            if (!new[] { ".ass", ".srt", ".ssa", ".sub", ".idx", ".vtt" }.Contains(System.IO.Path
                                    .GetExtension(potentialSubFile).ToLower()))
                            {
                                continue;
                            }
                            
                            // New method to try and get working later. This would remove the need for a symbolic link / copy of the subtitle file.
                            // Cant get the adding of the subtitle filepath to the library entry working.
                            /* 
                            if (!movie.SubtitleFiles.Contains(potentialSubFile))
                            {   
                                movie.SubtitleFiles = movie.SubtitleFiles.Append(potentialSubFile).ToArray();
                                movie.HasSubtitles = true;
                            }
                            */
                            
                            _logger.LogDebug("Found eligible subtitle file: {0}", potentialSubFile);

                            var newSubFilePath =
                                RemoveExtensionFromPath(System.IO.Path.GetFullPath(movie.Path),
                                    System.IO.Path.GetExtension(movie.Path)) + "." + System.IO.Path.GetFileName(potentialSubFile);
                            
                            
                            // Continue if file with same name already exists.
                            if (System.IO.File.Exists(newSubFilePath)) continue;

                            hasNewSubtitle = true;
                            
                            // Copy subtitle to movie folder
                            try
                            {
                                System.IO.File.CreateSymbolicLink(newSubFilePath, potentialSubFile);
                            }
                            catch (Exception ex)
                            {
                                // Try copy file if symbolic link creation fails
                                if (ex.GetType().IsAssignableFrom(typeof(IOException)))
                                {
                                    try
                                    {
                                        System.IO.File.Copy(potentialSubFile, newSubFilePath);  
                                    }
                                    catch (Exception e)
                                    {
                                        _logger.LogError(ex, "Error copying subtitle file {0}. Error: {1}", potentialSubFile, e.ToString());
                                    }   
                                }
                                else
                                {
                                    _logger.LogError(ex, "Error creating subtitle symbolic link {0}. Error: {1}", potentialSubFile, ex.ToString());
                                }
                                
                            }
                            
                            // Trigger a scan on this movie again so the new subtitle files will be discovered
                            
                        }
                    }

                if (hasNewSubtitle)
                {
                    movie.ChangedExternally();
                }
                
                ++completedCount;

                // calc percentage (current / maximum) * 100
                progress.Report((completedCount / moviesFound) * 100);
                
            }
            
            progress.Report(100);
            return Task.CompletedTask;
        }
        
        private static string RemoveExtensionFromPath(string input, string extension)
        {
            return input.EndsWith(extension) ? input[..input.LastIndexOf(extension, StringComparison.Ordinal)] : input;
        }
    }
}