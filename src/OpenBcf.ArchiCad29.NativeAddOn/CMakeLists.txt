# openBCF ArchiCAD 29 Add-On - built with the real CMake project shape used by the ArchiCAD 29 API
# DevKit's own example Add-Ons (confirmed against Examples/Selection_Manager/CMakeLists.txt in
# https://github.com/GRAPHISOFT/archicad-api-devkit, release 29.3100 - freely downloadable, no
# registration needed, contrary to what an earlier draft of this file assumed). Reconciled for real
# against that template; the one addition specific to this Add-On (the post-build copy of the
# OpenBcf.ArchiCad29.Helper publish output) is marked below. Everything else mirrors the DevKit
# template line-for-line.
#
# This Add-On does NOT host .NET in-process (see Src/HelperProcess.h for why - ArchiCAD cycles
# Initialize()/FreeData() independently of anything this Add-On does, most likely a full .apx DLL
# unload/reload, which a hosted CoreCLR runtime cannot survive), so unlike an earlier version of
# this file, no .NET SDK / hostfxr.h / coreclr_delegates.h / nethost.h is needed to build this
# project at all - it only launches OpenBcf.ArchiCad29.Helper.exe as a separate process and talks
# to it over named pipes.
#
# Usage:
#   dotnet publish ..\OpenBcf.ArchiCad29.Helper -c Release -r win-x64 --self-contained false
#   cmake -S . -B build -G "Visual Studio 17 2022" -A x64 ^
#     -DAC_API_DEVKIT_DIR="C:/Path/To/API-Development-Kit-29"
#   cmake --build build --config Release
# AC_API_DEVKIT_DIR must point at the extracted DevKit's root (the folder directly containing
# Support/ and Examples/), matching how the real template resolves it.

cmake_minimum_required (VERSION 3.16)

find_package (Python3 REQUIRED COMPONENTS Interpreter)
message (STATUS "Using Python: ${Python3_EXECUTABLE} (version ${Python3_VERSION})")

function (SetCompilerOptions target)
	target_compile_features (${target} PUBLIC cxx_std_20)
	target_compile_options (${target} PUBLIC "$<$<CONFIG:Debug>:-DDEBUG>")
	target_compile_options (${target} PUBLIC /W3 /WX /wd4996 /Zc:wchar_t-)
endfunction ()

function (AddGSModuleIncludeFolder target folder)
	message (STATUS "AddGSModuleIncludeFolder ${folder}")
	target_include_directories (${target} PUBLIC "${folder}")
endfunction ()

function (AddGSModuleLinkLibrary target library)
	file (GLOB libs "${library}/Win/*.lib")
	foreach (lib ${libs})
		message (STATUS "AddGSModuleLinkLibrary ${lib}")
		target_link_libraries (${target} ${lib})
	endforeach ()
endfunction ()

set_property (GLOBAL PROPERTY USE_FOLDERS ON)

get_filename_component (CMAKE_CURRENT_FOLDER_NAME "${CMAKE_CURRENT_LIST_DIR}" NAME)
set (CMAKE_SUPPRESS_REGENERATION 1)
set (CMAKE_CONFIGURATION_TYPES Debug;Release;RelWithDebInfo)
set (CMAKE_GENERATOR_TOOLSET v143)
set (CMAKE_MSVC_RUNTIME_LIBRARY MultiThreadedDLL)

set (AC_API_DEVKIT_DIR "${CMAKE_CURRENT_LIST_DIR}/../.." CACHE PATH "API DevKit directory.")
# openBCF addition: this folder is named OpenBcf.ArchiCad29.NativeAddOn, not "openBCF" - override
# the DevKit template's folder-name-derived default so the built .apx is still called openBCF.apx.
set (AC_ADDON_NAME "openBCF" CACHE STRING "Add-On name.")
set (AC_ADDON_LANGUAGE "INT" CACHE STRING "Add-On language code.")

message (STATUS "APIDevKit directory: ${AC_API_DEVKIT_DIR}")
set (ACAPINC_FILE_LOCATION ${AC_API_DEVKIT_DIR}/Support/Inc/ACAPinc.h)
if (EXISTS ${ACAPINC_FILE_LOCATION})
	file (READ ${ACAPINC_FILE_LOCATION} ACAPIncContent)
	string (REGEX MATCHALL "#define[ \t]+ServerMainVers_([0-9][0-9])" VersionList ${ACAPIncContent})
	set (ARCHICAD_VERSION ${CMAKE_MATCH_1})
	message (STATUS "Archicad Version: ${ARCHICAD_VERSION}")
else ()
	message (FATAL_ERROR "Failed to detect Archicad version, please check the value of the AC_API_DEVKIT_DIR variable.")
endif ()

add_definitions (-DUNICODE -D_UNICODE -D_ITERATOR_DEBUG_LEVEL=0 -DGS_WIN)
add_definitions (-DACExtension)

project (${AC_ADDON_NAME})

set (AddOnSourcesFolder ./Src)
set (AddOnResourcesFolder .)

# AddOnResources

set (ResourceObjectsDir ${CMAKE_BINARY_DIR}/ResourceObjects)

file (GLOB AddOnImageFiles
	${AddOnResourcesFolder}/RFIX/Images/*.svg
)
file (GLOB AddOnResourceFiles
	${AddOnResourcesFolder}/R${AC_ADDON_LANGUAGE}/*.grc
	${AddOnResourcesFolder}/RFIX/*.grc
	${AddOnResourcesFolder}/RFIX.win/*.rc2
)

source_group ("Images" FILES ${AddOnImageFiles})
source_group ("Resources" FILES ${AddOnResourceFiles})
add_custom_target (
	AddOnResources ALL
	DEPENDS "${ResourceObjectsDir}/AddOnResources.stamp"
	SOURCES ${AddOnResourceFiles} ${AddOnImageFiles}
)

get_filename_component (AddOnSourcesFolderAbsolute "${CMAKE_CURRENT_LIST_DIR}/${AddOnSourcesFolder}" ABSOLUTE)
get_filename_component (AddOnResourcesFolderAbsolute "${CMAKE_CURRENT_LIST_DIR}/${AddOnResourcesFolder}" ABSOLUTE)
get_filename_component (APIDevKitToolsFolderAbsolute "${AC_API_DEVKIT_DIR}/Support/Tools" ABSOLUTE)
add_custom_command (
	OUTPUT "${ResourceObjectsDir}/AddOnResources.stamp"
	DEPENDS ${AddOnResourceFiles} ${AddOnImageFiles}
	COMMENT "Compiling resources..."
	COMMAND ${CMAKE_COMMAND} -E make_directory "${ResourceObjectsDir}"
	COMMAND ${Python3_EXECUTABLE} "${APIDevKitToolsFolderAbsolute}/CompileResources.py" "${AC_ADDON_LANGUAGE}" "${AC_API_DEVKIT_DIR}" "${AddOnSourcesFolderAbsolute}" "${AddOnResourcesFolderAbsolute}" "${ResourceObjectsDir}" "${ResourceObjectsDir}/${AC_ADDON_NAME}.res"
	COMMAND ${CMAKE_COMMAND} -E touch "${ResourceObjectsDir}/AddOnResources.stamp"
)

# AddOn

file (GLOB AddOnHeaderFiles
	${AddOnSourcesFolder}/*.h
	${AddOnSourcesFolder}/*.hpp
)
file (GLOB AddOnSourceFiles
	${AddOnSourcesFolder}/*.c
	${AddOnSourcesFolder}/*.cpp
)
file (GLOB AllCFiles
	${AddOnSourcesFolder}/*.c
)
set_source_files_properties (${AllCFiles} PROPERTIES LANGUAGE CXX)
set (
	AddOnFiles
	${AddOnHeaderFiles}
	${AddOnSourceFiles}
)
source_group ("Sources" FILES ${AddOnFiles})
add_library (AddOn SHARED ${AddOnFiles})

set_target_properties (AddOn PROPERTIES OUTPUT_NAME ${AC_ADDON_NAME})
set_target_properties (AddOn PROPERTIES SUFFIX ".apx")

target_link_options (AddOn PUBLIC "${ResourceObjectsDir}/${AC_ADDON_NAME}.res")
target_link_options (AddOn PUBLIC /export:GetExportedFuncAddrs,@1 /export:SetImportedFuncAddrs,@2)

target_include_directories (AddOn PUBLIC
	${AddOnSourcesFolder}
	${AC_API_DEVKIT_DIR}/Support/Inc
)

target_link_libraries (AddOn
	"${AC_API_DEVKIT_DIR}/Support/Lib/ACAP_STAT.lib"
)

SetCompilerOptions (AddOn)
add_dependencies (AddOn AddOnResources)

get_filename_component (APIDevKitModulesDir "${AC_API_DEVKIT_DIR}/Support/Modules" ABSOLUTE)

file (GLOB GSIncludeFolders
	${APIDevKitModulesDir}/*
)
foreach (includeFolder ${GSIncludeFolders})
	AddGSModuleIncludeFolder (AddOn ${includeFolder})
endforeach ()

file (GLOB GSLinkLibFolders
	${APIDevKitModulesDir}/*
)
foreach (linkLibFolder ${GSLinkLibFolders})
	AddGSModuleLinkLibrary (AddOn ${linkLibFolder})
endforeach ()

get_filename_component (new_hpp "${APIDevKitModulesDir}/GSRoot/GSNew.hpp" REALPATH)
get_filename_component (malloc_hpp "${APIDevKitModulesDir}/GSRoot/GSMalloc.hpp" REALPATH)
target_compile_options (
	AddOn PRIVATE
	"SHELL:/FI \"${new_hpp}\""
	"SHELL:/FI \"${malloc_hpp}\""
)

# --- openBCF addition: drops the helper's self-contained publish output (including
# OpenBcf.ArchiCad29.Helper.exe) next to the compiled .apx so HelperProcess::LaunchHelperProcess
# (which resolves the exe relative to this Add-On's own directory, via ACAPI_GetOwnLocation - same
# pattern AddOnMain.cpp's GetAddOnDirectory uses) finds it. Run
# `dotnet publish ../OpenBcf.ArchiCad29.Helper -c Release -r win-x64 --self-contained false`
# first. -------------------------------------------------------------------------------------------
add_custom_command (TARGET AddOn POST_BUILD
	COMMAND ${CMAKE_COMMAND} -E copy_directory
		"${CMAKE_CURRENT_SOURCE_DIR}/../OpenBcf.ArchiCad29.Helper/bin/Release/net8.0-windows/win-x64/publish"
		"$<TARGET_FILE_DIR:AddOn>"
	COMMENT "Copying OpenBcf.ArchiCad29.Helper publish output next to the built .apx"
)
# --- end openBCF addition --------------------------------------------------------------------------
