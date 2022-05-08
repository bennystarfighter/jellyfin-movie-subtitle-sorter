using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Plugin.Template.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;


namespace Jellyfin.Plugin.SubtitleFixer
{
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        public override string Name => "Movie subtitle sorter";

        public override Guid Id => Guid.Parse("786e0827-ed4b-4cbc-870b-12c186f47894");

        public override string Description => "Looks through all MOVIE libraries for subtitles hidden in subfolders and copies them with a working name.";

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
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<SubtitleFixer> _logger;
        private readonly IFileSystem _fileSystem;
        private readonly IProviderManager _providerManager;
        private readonly NamingOptions _namingOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="OrganizerScheduledTask"/> class.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1611:Element parameters should be documented", Justification = "Parameter types/names are self-documenting")]
        public SubtitleFixer(
            ILibraryMonitor libraryMonitor,
            ILibraryManager libraryManager,
            ILoggerFactory loggerFactory,
            IFileSystem fileSystem,
            IProviderManager providerManager)
        {
            _libraryMonitor = libraryMonitor;
            _libraryManager = libraryManager;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SubtitleFixer>();
            _fileSystem = fileSystem;
            _providerManager = providerManager;
            _namingOptions = new NamingOptions();
        }

        Task IScheduledTask.Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            int SubtitlesFixed = 0;

            var AllLibraries = _libraryManager.GetVirtualFolders();

            List<VirtualFolderInfo> MovieLibraries = new List<VirtualFolderInfo>();

            foreach (var library in AllLibraries)
            {
                if (library.CollectionType == CollectionTypeOptions.Movies)
                {
                    MovieLibraries.Add(library);
                    _logger.Log(LogLevel.Information, "Movie libraries: {0}", library.Name);
                }
            }

            foreach (var library in MovieLibraries)
            {
                foreach (var libraryLocation in library.Locations)
                {
                    _logger.LogDebug("Checking librabry location: {0}", libraryLocation);
                    _logger.LogDebug("{0} subfolders: {0}", library.Name, System.IO.Directory.GetDirectories(libraryLocation));

                    foreach (var movieFolder in System.IO.Directory.GetDirectories(libraryLocation))
                    {
                        _logger.LogDebug("Checking folder: {0}", movieFolder);
                        bool foundMovieFile = false;
                        string movieFilePath = "";
                        foreach (var file in System.IO.Directory.GetFiles(movieFolder))
                        {
                            _logger.LogDebug("Checking file: {0}", file);
                            _logger.LogDebug("Extension: {0}", System.IO.Path.GetExtension(file).ToLower());
                            try
                            {
                                if (new string[] { ".mkv", ".mp4", ".webm" }.Contains(System.IO.Path.GetExtension(file).ToLower()))
                                {
                                    foundMovieFile = true;
                                    movieFilePath = file;
                                    _logger.LogDebug("Is a movie: {0}", file);
                                    break;
                                }
                                else
                                {
                                    _logger.LogDebug("Not a movie: {0}", file);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error checking if file is video {0}", file);
                            }
                        }

                        if (foundMovieFile)
                        {
                            _logger.LogDebug("Found movie: {0}", movieFilePath);

                            foreach (var subFolder in System.IO.Directory.GetDirectories(movieFolder))
                            {
                                foreach (var file in System.IO.Directory.GetFiles(subFolder))
                                {
                                    if (new string[] { ".ass", ".srt", ".ssa", ".sub", ".idx", ".vtt" }.Contains(System.IO.Path.GetExtension(file).ToLower()))
                                    {
                                        _logger.LogDebug("Found subtitle file: {0}", file);
                                        string newSubFilePath = RemoveExtensionFromPath(System.IO.Path.GetFullPath(movieFilePath), System.IO.Path.GetExtension(movieFilePath)) + "." + System.IO.Path.GetFileName(file);
                                        _logger.LogDebug("New subtitle path: {0}", newSubFilePath);

                                        if (!System.IO.File.Exists(newSubFilePath))
                                        {
                                            try
                                            {
                                                System.IO.File.Copy(file, newSubFilePath);
                                                _logger.LogDebug("Copied subtitle to: {0}", newSubFilePath);
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, "Error copying subtitle file {0}", file);
                                            }

                                            _logger.LogDebug("Found unused subtitle, new file: {0}", newSubFilePath);
                                            SubtitlesFixed++;
                                        } else
                                        {
                                            _logger.LogDebug("new subtitle file already exists");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            
            if (!_libraryManager.IsScanRunning)
            {
                _libraryManager.QueueLibraryScan();
            }

            _logger.LogInformation("Number of new subtitles created: {0}", SubtitlesFixed); 

            return Task.CompletedTask;
        }

        IEnumerable<TaskTriggerInfo> IScheduledTask.GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(12).Ticks
            };
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
            } else
            {
                return input;
            }
        }
    }
}
