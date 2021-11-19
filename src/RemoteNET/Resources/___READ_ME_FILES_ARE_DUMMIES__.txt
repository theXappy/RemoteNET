The files in this directory have the expected names as some resources used in the .csproj file.
To prevent VisualStudio from complaining about missing underlying files for the resources those placeholders are in place.
They aren't really dlls/exes/zips when the project is "idle" -- When building starts those files are replaced
by the real dlls/exes/zips that we want to compile as resources.
After the build is over those files' contents are re-written so we don't leave stale files in this directory.

All of this logic is done using Pre-build and Post-build events in the csproj.