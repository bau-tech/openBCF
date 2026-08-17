// *****************************************************************************
// General settings for AddOn developments
//
// Copied verbatim from the real ArchiCAD 29 API DevKit's own example Add-Ons
// (Examples/Selection_Manager/Src/APIEnvir.h etc.) - this is a project-local file every Add-On
// provides itself, not a DevKit-supplied header, confirmed the hard way: an earlier draft of this
// project assumed ACAPinc.h would provide it and failed to compile with a real MSVC + the real
// DevKit (error C1083, file not found) until this was added.
// *****************************************************************************

#ifndef	_APIENVIR_H_
#define	_APIENVIR_H_


#if defined (_MSC_VER)
	#if !defined (WINDOWS)
		#define WINDOWS
	#endif
#endif

#if defined (WINDOWS)
	#include "Win32Interface.hpp"
#endif

#if defined (macintosh)
	#include <CoreServices/CoreServices.h>
#endif

#if !defined (ACExtension)
	#define ACExtension
#endif


#endif
