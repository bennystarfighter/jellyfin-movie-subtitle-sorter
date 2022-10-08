using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Jellyfin.Plugin.SubtitleFixer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleFixer
{
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public override string Name => "Movie subtitle sorter";

        public override Guid Id => Guid.Parse("786e0827-ed4b-4cbc-870b-12c186f47894");

        public override string Description =>
            "Looks through all movie libraries for subtitles hidden in subfolders and copies them with a working name.";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
            xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }
    }

    public class SubtitleFixer : IScheduledTask
    {
        string IScheduledTask.Name => "Run movie subtitle sorter.";
        string IScheduledTask.Key => "SubtitleFixerAutoSort";

        string IScheduledTask.Description =>
            "Looks through all MOVIE libraries for subtitles hidden in subfolders and copies them with a working name.";

        string IScheduledTask.Category => "Library";

        private readonly ILibraryMonitor _libraryMonitor;

        private readonly ILibraryManager _libraryManager;

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
            IFileSystem fileSystem
        )
        {
            _libraryMonitor = libraryMonitor;
            _libraryManager = libraryManager;
            // _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SubtitleFixer>();
            _fileSystem = fileSystem;
            // _providerManager = providerManager;
            // _namingOptions = new NamingOptions();
        }

        Task IScheduledTask.ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
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
            _logger.LogInformation("Found [{0}] movies", moviesFound);

            foreach (var movie in allMovies)
            {
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


                var dirName = System.IO.Path.GetDirectoryName(movie.Path);
                if (dirName != null)
                    // loop through subfolder inside movie folder
                    foreach (var subFolder in System.IO.Directory.GetDirectories(dirName))
                    {
                        // Check all files in subfolder
                        foreach (var sourceFile in System.IO.Directory.GetFiles(subFolder))
                        {
                            if (!new[] { ".ass", ".srt", ".ssa", ".sub", ".idx", ".vtt" }.Contains(System.IO.Path
                                    .GetExtension(sourceFile).ToLower()))
                            {
                                continue;
                            }

                            if (!movie.SubtitleFiles.Contains(sourceFile))
                            {
                                movie.SubtitleFiles = movie.SubtitleFiles.Append(sourceFile).ToArray();
                                movie.HasSubtitles = true;
                            }

                            var updateCancellationToken = new CancellationToken();
                            _libraryManager
                                .UpdateItemAsync(movie, movie.GetParent(), ItemUpdateType.MetadataEdit, updateCancellationToken)
                                .Wait(updateCancellationToken);

                            /*
                            var newSubFilePath =
                                RemoveExtensionFromPath(System.IO.Path.GetFullPath(movie.Path),
                                    System.IO.Path.GetExtension(movie.Path)) + "." + System.IO.Path.GetFileName(sourceFile);

                            // Continue if file with same name already exists.
                            if (System.IO.File.Exists(newSubFilePath)) continue;

                            // Copy subtitle to movie folder
                            try
                            {
                                System.IO.File.Copy(sourceFile, newSubFilePath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error copying subtitle file {}", sourceFile);
                            }
                            */
                        }
                    }

                ++completedCount;

                // calc percentage (current / maximum) * 100
                progress.Report((completedCount / moviesFound) * 100);
            }

            if (!_libraryManager.IsScanRunning)
            {
                var libraryScanCancellationToken = new CancellationToken();
                IProgress<double> scanProgress = new Progress<double>();
                _libraryManager.ValidateMediaLibrary(scanProgress, libraryScanCancellationToken);
            }

            return Task.CompletedTask;
        }

        IEnumerable<TaskTriggerInfo> IScheduledTask.GetDefaultTriggers()
        {
            var info = new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(12).Ticks
            };
            yield return info;
        }

        /*
        private bool IsPathAlreadyInMediaLibrary(string path, List<string> libraryFolderPaths)
        {
            return libraryFolderPaths.Any(i => string.Equals(i, path, StringComparison.Ordinal) || _fileSystem.ContainsSubPath(i, path));
        }
        */

        private static string RemoveExtensionFromPath(string input, string extension)
        {
            return input.EndsWith(extension) ? input[..input.LastIndexOf(extension, StringComparison.Ordinal)] : input;
        }
    }
}