using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities.Libraries;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SubtitleFixer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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

        public override string Description => "Looks through all movie libraries for subtitles hidden in subfolders and copies them with a working name.";

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }
    }

    public class SubtitleFixer : IScheduledTask
    {
        string IScheduledTask.Name { get { return "Run movie subtitle sorter."; } }

        string IScheduledTask.Key { get { return "SubtitleFixerAutoSort"; } }

        string IScheduledTask.Description { get { return "Looks through all MOVIE libraries for subtitles hidden in subfolders and copies them with a working name."; } }

        string IScheduledTask.Category { get { return "Library"; } }

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
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented", Justification = "Parameter types/names are self-documenting")]
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
            List<VirtualFolderInfo> allVirtualFolders = _libraryManager.GetVirtualFolders();

            // Run for movies
            List<MediaBrowser.Controller.Entities.Movies.Movie> allMovies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie }, IsVirtualItem = false, OrderBy = new List<ValueTuple<string, SortOrder>> { new ValueTuple<string, SortOrder>(ItemSortBy.SortName, SortOrder.Ascending) }, Recursive = true,
            }, false).Select(m => m as MediaBrowser.Controller.Entities.Movies.Movie).ToList();

            int moviesFound = allMovies.Count;
            int completedCount = 0;
            _logger.LogInformation("Found [{0}] movies", moviesFound);

            foreach (var movie in allMovies)
            {
                DirectoryInfo movieDir = System.IO.Directory.GetParent(movie.Path);
                if (movieDir == null)
                {
                    continue;
                }
                string movieParent = movieDir.FullName;

                // Check if movie is in root folder of movie library location.
                bool isInRoot = false;
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
                if (isInRoot)
                {
                    continue;
                }
                // END

                foreach (var subFolder in System.IO.Directory.GetDirectories(System.IO.Path.GetDirectoryName(movie.Path)))
                {
                    foreach (var file in System.IO.Directory.GetFiles(subFolder))
                    {
                        if (new string[] { ".ass", ".srt", ".ssa", ".sub", ".idx", ".vtt" }.Contains(System.IO.Path.GetExtension(file).ToLower()))
                        {
                            string newSubFilePath = RemoveExtensionFromPath(System.IO.Path.GetFullPath(movie.Path), System.IO.Path.GetExtension(movie.Path)) + "." + System.IO.Path.GetFileName(file);
                            if (!System.IO.File.Exists(newSubFilePath))
                            {
                                try
                                {
                                    System.IO.File.Copy(file, newSubFilePath);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error copying subtitle file {0}", file);
                                }
                            }
                        }
                    }
                }

                ++completedCount;

                // calc percentage (current / maximum) * 100
                progress.Report((completedCount / moviesFound) * 100);
            }

            /*
            if (!_libraryManager.IsScanRunning)
            {
                _libraryManager.ValidateMediaLibrary();
            }
            */

            return Task.CompletedTask;
        }

        IEnumerable<TaskTriggerInfo> IScheduledTask.GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(12).Ticks };
        }

        private bool IsPathAlreadyInMediaLibrary(string path, List<string> libraryFolderPaths)
        {
            return libraryFolderPaths.Any(i => string.Equals(i, path, StringComparison.Ordinal) || _fileSystem.ContainsSubPath(i, path));
        }

        private string RemoveExtensionFromPath(string input, string extension)
        {
            if (input.EndsWith(extension))
            {
                return input.Substring(0, input.LastIndexOf(extension));
            }
            else
            {
                return input;
            }
        }
    }
}
