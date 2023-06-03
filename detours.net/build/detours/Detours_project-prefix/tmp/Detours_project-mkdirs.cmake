# Distributed under the OSI-approved BSD 3-Clause License.  See accompanying
# file Copyright.txt or https://cmake.org/licensing for details.

cmake_minimum_required(VERSION 3.5)

file(MAKE_DIRECTORY
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src/Detours_project"
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src/Detours_project/src"
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix"
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/tmp"
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src/Detours_project-stamp"
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src"
  "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src/Detours_project-stamp"
)

set(configSubDirs Debug;Release;MinSizeRel;RelWithDebInfo)
foreach(subDir IN LISTS configSubDirs)
    file(MAKE_DIRECTORY "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src/Detours_project-stamp/${subDir}")
endforeach()
if(cfgdir)
  file(MAKE_DIRECTORY "C:/git/RemoteNET/detours.net/build/detours/Detours_project-prefix/src/Detours_project-stamp${cfgdir}") # cfgdir has leading slash
endif()
