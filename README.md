# THIS IS NOT MAINTAINED ANYMORE

Please move to the new and improved plugin: [jellyfin-subtitle-sorter](https://github.com/bennystarfighter/jellyfin-subtitle-sorter)

<br><br><br>
# Jellyfin movie-subtitle-sorter plugin

## Info

#### Jellyfin plugin repository can be found here: https://github.com/bennystarfighter/Jellyfin-Plugin-Repository

This plugin will check all movie folders with a subfolder containing subtitle files.
If it finds a subtitle file it will copy it to the same folder as the movie file with the movie filename and the subtitle filename combined to make sure Jellyfin finds it when scanning the library.
It will try and use symbolic links first but will fall back to conventional copying if it encounters problems.

~~Per default it runs every 12-hours as a scheduled task.~~

The plugin runs automatically after a library scan.

It looks for any files with these file extensions: 

**.ass | .srt | .ssa | .sub | .idx | .vtt**

## Example:
**Before**
```
Best.Movie.Ever
├── Best.Movie.Ever.1080p.x265.mkv
│   └── Subs
│       └── English.srt
│       └── Spanish.ssa
```

**After**
```
Best.Movie.Ever
├── Best.Movie.Ever.1080p.x265.mkv
├── Best.Movie.Ever.1080p.x265.English.srt
├── Best.Movie.Ever.1080p.x265.Spanish.ssa
│   └── Subs
│       └── English.srt
│       └── Spanish.ssa
```

# Disclaimer
**I am not responsible for anything that may happen to your jellyfin server, your media library, 
your cat or anything else as a result of using this plugin.
Check out the code and use it at your own risk.**
